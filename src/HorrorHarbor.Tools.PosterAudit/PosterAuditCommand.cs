using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorrorHarbor.Posters;

namespace HorrorHarbor.Tools.PosterAudit;

public static class PosterAuditCommand
{
    private const int MaximumCatalogEntries = 10_000;
    private const int NearDuplicateDistance = 4;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            AuditOptions options = AuditOptions.Parse(args);
            PosterAuditManifest manifest = await GenerateAsync(options).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(manifest.Summary, AuditJsonContext.Default.PosterAuditSummary));
            return manifest.Failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Poster audit failed: {exception.Message}");
            return 2;
        }
    }

    public static async Task<PosterAuditManifest> GenerateAsync(
        AuditOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        string catalogPath = Path.GetFullPath(options.CatalogPath);
        string outputPath = Path.GetFullPath(options.OutputPath);
        string postersPath = Path.Combine(outputPath, "posters");
        string sheetsPath = Path.Combine(outputPath, "contact-sheets");
        if (Directory.Exists(outputPath) &&
            Directory.EnumerateFileSystemEntries(outputPath).Any())
        {
            throw new IOException(
                "The poster-audit output directory must not already contain files. " +
                "Choose a new or empty directory; the tool never recursively deletes user-supplied paths.");
        }

        Directory.CreateDirectory(postersPath);
        Directory.CreateDirectory(sheetsPath);

        byte[] catalogBytes = await File.ReadAllBytesAsync(catalogPath, cancellationToken).ConfigureAwait(false);
        AuditCandidate[] candidates = JsonSerializer.Deserialize(catalogBytes, AuditJsonContext.Default.AuditCandidateArray)
            ?? throw new InvalidDataException("Poster catalog deserialized to null.");
        if (candidates.Length is 0 or > MaximumCatalogEntries)
        {
            throw new InvalidDataException($"Catalog entry count must be between 1 and {MaximumCatalogEntries:N0}.");
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> corpus = EvaluationCorpusSelector.Select(
            candidates,
            Math.Min(options.EvaluationCorpusSize, candidates.Length));
        var entries = new List<PosterAuditEntry>(candidates.Length);
        var failures = new List<PosterAuditFailure>();
        var hashes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        int cacheHits = 0;
        using var fallback = new LocalPosterService(new PosterCacheOptions(
            postersPath,
            options.Width,
            options.Height,
            options.MaximumFileBytes));
        using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        using var official = new TmdbPosterService(
            httpClient,
            new WindowsCredentialPosterCredentialStore(),
            TmdbPosterOptions.CreateDefault());
        IPosterService service = options.UseTmdb
            ? new HybridPosterService(official, fallback)
            : fallback;

        var totalStopwatch = Stopwatch.StartNew();
        foreach (AuditCandidate candidate in candidates.OrderBy(static item => item.CandidateId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var descriptor = new PosterDescriptor(
                    candidate.CandidateId,
                    candidate.Title,
                    candidate.ReleaseYear,
                    candidate.PrimaryLens,
                    candidate.PosterTone,
                    candidate.Tags,
                    SecondaryLens: candidate.SecondaryLens,
                    Franchise: candidate.Franchise,
                    ScareIntensity: candidate.ScareIntensity,
                    GoreIntensity: candidate.GoreIntensity,
                     DreadIntensity: candidate.DreadIntensity);
                descriptor = descriptor with
                {
                    OriginalTitle = candidate.OriginalTitle,
                    OriginalLanguage = candidate.OriginalLanguage,
                    TmdbId = candidate.TmdbId,
                };
                GeneratedPosterArtDirection artDirection = GeneratedPosterArtDirector.Analyze(descriptor);
                var stopwatch = Stopwatch.StartNew();
                PosterAsset asset = await service.GetPosterAsync(descriptor, cancellationToken).ConfigureAwait(false);
                if (!asset.WasGenerated)
                {
                    cacheHits++;
                }

                stopwatch.Stop();
                byte[] encoded = await File.ReadAllBytesAsync(asset.FilePath, cancellationToken).ConfigureAwait(false);
                string sha256 = Convert.ToHexString(SHA256.HashData(encoded));
                ulong perceptualHash = PerceptualHash.Calculate(encoded, options.Width, options.Height);
                string relativePath = Path.GetRelativePath(outputPath, asset.FilePath).Replace('\\', '/');
                corpus.TryGetValue(candidate.CandidateId, out IReadOnlyList<string>? corpusReasons);
                PosterProvenance? provenance = asset.Provenance;
                bool provenanceComplete = provenance is not null &&
                    !string.IsNullOrWhiteSpace(provenance.Provider) &&
                    !string.IsNullOrWhiteSpace(provenance.MatchMethod) &&
                    !string.IsNullOrWhiteSpace(provenance.RetrievedUtc) &&
                    !string.IsNullOrWhiteSpace(provenance.ContentSha256) &&
                    string.Equals(provenance.ContentSha256, sha256, StringComparison.OrdinalIgnoreCase);
                entries.Add(new PosterAuditEntry(
                    candidate.CandidateId,
                    candidate.Title,
                    candidate.ReleaseYear,
                    candidate.PrimaryLens,
                    candidate.Franchise,
                    relativePath,
                    sha256,
                    perceptualHash.ToString("X16", CultureInfo.InvariantCulture),
                    encoded.LongLength,
                    Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    corpusReasons is not null,
                    corpusReasons ?? Array.Empty<string>(),
                    asset.SourceKind.ToString(),
                    provenance?.Provider ?? "Unknown",
                    provenance?.MatchMethod ?? "missing",
                    provenance?.MatchConfidence ?? 0,
                    provenanceComplete,
                    artDirection.Family.ToString(),
                    artDirection.Motif,
                    artDirection.EraStyle.ToString(),
                    artDirection.TitleStyle.ToString()));
                hashes[candidate.CandidateId] = perceptualHash;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or ArgumentException or OverflowException)
            {
                failures.Add(new PosterAuditFailure(
                    candidate.CandidateId,
                    candidate.Title,
                    exception.GetType().Name,
                    exception.Message));
            }
        }

        totalStopwatch.Stop();
        SimilarityPair[] similarities = FindSimilarities(entries, hashes);
        int exactGroups = entries.GroupBy(static entry => entry.Sha256, StringComparer.Ordinal).Count(static group => group.Count() > 1);
        int uniquePerceptualHashes = entries.Select(static entry => entry.PerceptualHash).Distinct(StringComparer.Ordinal).Count();
        int contactSheetCount = WriteContactSheets(entries, sheetsPath, options.PageSize, options.Width, options.Height);
        string catalogSha256 = Convert.ToHexString(SHA256.HashData(catalogBytes));
        double[] timings = entries.Select(static entry => entry.RenderMilliseconds).Order().ToArray();
        Dictionary<string, int> familyDistribution = entries
            .GroupBy(static entry => entry.CompositionFamily, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        int semanticStyleCombinations = entries
            .Select(static entry => string.Join('|', entry.CompositionFamily, entry.Motif, entry.EraStyle, entry.TitleStyle))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var summary = new PosterAuditSummary(
            options.UseTmdb ? "P3-personal-hybrid-artwork" : "P3-generated-artwork",
            ProceduralPosterRenderer.RenderVersion,
            catalogSha256,
            candidates.Length,
            entries.Count,
            entries.Count(static entry => entry.SourceKind == nameof(PosterSourceKind.TmdbOfficial)),
            entries.Count(static entry => entry.SourceKind == nameof(PosterSourceKind.GeneratedFallback)),
            entries.Count(static entry => entry.ProvenanceComplete),
            familyDistribution,
            semanticStyleCombinations,
            failures.Count,
            exactGroups,
            similarities.Count(static pair => !pair.ExactDuplicate),
            uniquePerceptualHashes,
            corpus.Count,
            contactSheetCount,
            Math.Round(totalStopwatch.Elapsed.TotalMilliseconds, 3),
            timings.Length == 0 ? 0 : Math.Round(timings.Average(), 3),
            Percentile(timings, 0.95),
            entries.Sum(static entry => entry.FileBytes),
            failures.Count == 0 ? "PASS" : "FAIL",
            ResolveTimestamp(),
            options.ReproductionCommand);
        CorpusEntry[] corpusEntries = entries
            .Where(static entry => entry.IsEvaluationCorpus)
            .Select(static entry => new CorpusEntry(entry.CandidateId, entry.Title, entry.ReleaseYear, entry.EvaluationReasons))
            .ToArray();
        var cache = new PosterCacheMetrics(
            entries.Count,
            entries.Count - cacheHits,
            cacheHits,
            entries.Count == 0 ? 0 : Math.Round(cacheHits / (double)entries.Count, 4),
            entries.Sum(static entry => entry.FileBytes));
        var manifest = new PosterAuditManifest(summary, cache, entries, failures, similarities, corpusEntries);

        await WriteJsonAsync(Path.Combine(outputPath, "manifest.json"), manifest, AuditJsonContext.Default.PosterAuditManifest, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(outputPath, "summary.json"), summary, AuditJsonContext.Default.PosterAuditSummary, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(outputPath, "cache.json"), cache, AuditJsonContext.Default.PosterCacheMetrics, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(outputPath, "posters.json"), entries.ToArray(), AuditJsonContext.Default.PosterAuditEntryArray, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(outputPath, "failures.json"), failures.ToArray(), AuditJsonContext.Default.PosterAuditFailureArray, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(outputPath, "similarities.json"), similarities, AuditJsonContext.Default.SimilarityPairArray, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(outputPath, "evaluation-corpus.json"), corpusEntries, AuditJsonContext.Default.CorpusEntryArray, cancellationToken).ConfigureAwait(false);
        await WriteTextAsync(Path.Combine(outputPath, "index.html"), BuildHtml(summary, entries, similarities, contactSheetCount), cancellationToken).ConfigureAwait(false);
        await WriteTextAsync(Path.Combine(outputPath, "README.md"), BuildReadme(summary), cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    internal static SimilarityPair[] FindSimilarities(
        List<PosterAuditEntry> entries,
        Dictionary<string, ulong> hashes)
    {
        var results = new List<SimilarityPair>();
        for (int first = 0; first < entries.Count; first++)
        {
            for (int second = first + 1; second < entries.Count; second++)
            {
                PosterAuditEntry a = entries[first];
                PosterAuditEntry b = entries[second];
                int distance = PerceptualHash.Distance(hashes[a.CandidateId], hashes[b.CandidateId]);
                bool exact = string.Equals(a.Sha256, b.Sha256, StringComparison.Ordinal);
                if (exact || distance <= NearDuplicateDistance)
                {
                    results.Add(new SimilarityPair(
                        a.CandidateId,
                        a.Title,
                        b.CandidateId,
                        b.Title,
                        distance,
                        exact));
                }
            }
        }

        return results
            .OrderBy(static pair => pair.HammingDistance)
            .ThenBy(static pair => pair.CandidateIdA, StringComparer.Ordinal)
            .ThenBy(static pair => pair.CandidateIdB, StringComparer.Ordinal)
            .ToArray();
    }

    private static int WriteContactSheets(
        List<PosterAuditEntry> entries,
        string outputPath,
        int pageSize,
        int sourceWidth,
        int sourceHeight)
    {
        const int columns = 5;
        const int tileWidth = 180;
        const int posterWidth = 150;
        int posterHeight = (int)Math.Round(posterWidth * (double)sourceHeight / sourceWidth);
        int tileHeight = posterHeight + 66;
        int pageCount = (entries.Count + pageSize - 1) / pageSize;
        for (int page = 0; page < pageCount; page++)
        {
            PosterAuditEntry[] pageEntries = entries.Skip(page * pageSize).Take(pageSize).ToArray();
            int rows = (pageEntries.Length + columns - 1) / columns;
            int canvasWidth = columns * tileWidth + 40;
            int canvasHeight = rows * tileHeight + 76;
            var svg = new StringBuilder();
            svg.AppendLine(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{canvasWidth}\" height=\"{canvasHeight}\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\">");
            svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#090b10\"/>");
            svg.AppendLine(CultureInfo.InvariantCulture, $"<text x=\"20\" y=\"31\" fill=\"#f7f8fa\" font-family=\"Segoe UI, sans-serif\" font-size=\"18\" font-weight=\"700\">Nocturne Harbor P2 generated art — page {page + 1} of {pageCount}</text>");
            for (int index = 0; index < pageEntries.Length; index++)
            {
                PosterAuditEntry entry = pageEntries[index];
                int x = 20 + index % columns * tileWidth;
                int y = 52 + index / columns * tileHeight;
                string imagePath = "../" + entry.RelativePath;
                svg.AppendLine(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{y}\" width=\"{posterWidth}\" height=\"{posterHeight}\" rx=\"5\" fill=\"#191e29\" stroke=\"#2f3848\"/>");
                svg.AppendLine(CultureInfo.InvariantCulture, $"<image href=\"{Xml(imagePath)}\" x=\"{x}\" y=\"{y}\" width=\"{posterWidth}\" height=\"{posterHeight}\" preserveAspectRatio=\"xMidYMid slice\"/>");
                svg.AppendLine(CultureInfo.InvariantCulture, $"<text x=\"{x}\" y=\"{y + posterHeight + 20}\" fill=\"#f7f8fa\" font-family=\"Segoe UI, sans-serif\" font-size=\"12\" font-weight=\"600\">{Xml(Truncate(entry.Title, 22))}</text>");
                svg.AppendLine(CultureInfo.InvariantCulture, $"<text x=\"{x}\" y=\"{y + posterHeight + 38}\" fill=\"#c0c8d4\" font-family=\"Segoe UI, sans-serif\" font-size=\"11\">{entry.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "year unknown"} · {Xml(Truncate(entry.CompositionFamily, 21))}</text>");
                if (entry.IsEvaluationCorpus)
                {
                    svg.AppendLine(CultureInfo.InvariantCulture, $"<circle cx=\"{x + posterWidth - 10}\" cy=\"{y + 10}\" r=\"6\" fill=\"#f4bc61\"><title>Evaluation corpus</title></circle>");
                }
            }

            svg.AppendLine("</svg>");
            File.WriteAllText(Path.Combine(outputPath, $"page-{page + 1:000}.svg"), svg.ToString(), new UTF8Encoding(false));
        }

        return pageCount;
    }

    private static string BuildHtml(
        PosterAuditSummary summary,
        List<PosterAuditEntry> entries,
        SimilarityPair[] similarities,
        int pageCount)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Nocturne Harbor P2 generated-art audit</title>
            <style>body{margin:0;background:#090b10;color:#f7f8fa;font:15px Segoe UI,sans-serif}main{max-width:1280px;margin:auto;padding:32px}h1,h2{margin-bottom:.35em}.muted{color:#c0c8d4}.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px}.stat{background:#141821;border:1px solid #2f3848;border-radius:10px;padding:16px}.pages{display:grid;gap:24px;margin-top:24px}.pages img{width:100%;background:#141821;border:1px solid #2f3848}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:8px;border-bottom:1px solid #2f3848}code{color:#f4bc61}</style>
            </head><body><main><h1>Nocturne Harbor P2 generated-art audit</h1>
            <p class="muted">Build P2 evidence for metadata-driven composition, typography, and semantic diversity. PASS means the evidence pipeline completed; visual acceptance still requires human review.</p><div class="stats">
            """);
        AddStat(html, "Catalog", summary.CatalogCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Generated", summary.GeneratedCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Official artwork", summary.OfficialArtworkCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Generated fallback", summary.GeneratedFallbackCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Complete provenance", summary.CompleteProvenanceCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Composition families", summary.CompositionFamilyDistribution.Count.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Semantic styles", summary.SemanticStyleCombinations.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Failures", summary.FailedCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Evaluation corpus", summary.EvaluationCorpusCount.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Near duplicates", summary.NearDuplicatePairs.ToString("N0", CultureInfo.InvariantCulture));
        AddStat(html, "Mean render", summary.MeanRenderMilliseconds.ToString("0.0 'ms'", CultureInfo.InvariantCulture));
        html.AppendLine("</div><h2>Composition-family distribution</h2><table><thead><tr><th>Family</th><th>Posters</th></tr></thead><tbody>");
        foreach ((string family, int count) in summary.CompositionFamilyDistribution)
        {
            html.AppendLine(CultureInfo.InvariantCulture, $"<tr><td>{Web(family)}</td><td>{count}</td></tr>");
        }

        html.AppendLine("</tbody></table><h2>Contact sheets</h2><div class=\"pages\">");
        for (int page = 1; page <= pageCount; page++)
        {
            html.AppendLine(CultureInfo.InvariantCulture, $"<a href=\"contact-sheets/page-{page:000}.svg\"><img loading=\"lazy\" alt=\"Poster contact sheet page {page}\" src=\"contact-sheets/page-{page:000}.svg\"></a>");
        }

        html.AppendLine("</div><h2>Similarity findings</h2><table><thead><tr><th>Distance</th><th>First</th><th>Second</th></tr></thead><tbody>");
        foreach (SimilarityPair pair in similarities.Take(200))
        {
            html.AppendLine(CultureInfo.InvariantCulture, $"<tr><td>{pair.HammingDistance}</td><td>{Web(pair.TitleA)}</td><td>{Web(pair.TitleB)}</td></tr>");
        }

        html.AppendLine(CultureInfo.InvariantCulture, $"</tbody></table><p class=\"muted\">Manifest includes {entries.Count:N0} poster records. Review <code>manifest.json</code>, <code>similarities.json</code>, and <code>evaluation-corpus.json</code> for machine-readable evidence.</p></main></body></html>");
        return html.ToString();
    }

    private static string BuildReadme(PosterAuditSummary summary) => $"""
        # Poster audit Build P2

        Status: **{summary.Status}**

        This folder is reproducible generated-art evidence for renderer version {summary.RendererVersion}. It records composition, motif, era, typography, provenance, and similarity evidence; it does not by itself certify production visual quality.

        - Catalog entries: {summary.CatalogCount:N0}
        - Generated: {summary.GeneratedCount:N0}
        - Official artwork: {summary.OfficialArtworkCount:N0}
        - Generated fallback: {summary.GeneratedFallbackCount:N0}
        - Complete provenance: {summary.CompleteProvenanceCount:N0}
        - Composition families: {summary.CompositionFamilyDistribution.Count:N0}
        - Semantic style combinations: {summary.SemanticStyleCombinations:N0}
        - Failed: {summary.FailedCount:N0}
        - Evaluation corpus: {summary.EvaluationCorpusCount:N0}
        - Near-duplicate pairs: {summary.NearDuplicatePairs:N0}
        - Exact duplicate groups: {summary.ExactDuplicateGroups:N0}
        - Catalog SHA-256: `{summary.CatalogSha256}`

        Open `index.html` for human review. Review `cache.json` for cold-baseline cache metrics. Poster bytes and hashes are deterministic. Timing and generation timestamp are environmental evidence and are not expected to be byte-identical between runs.

        PASS means that the technical evidence pipeline completed without a missing or invalid poster. Human contact-sheet and in-app crop review remain separate acceptance gates.

        Reproduce:

        ```text
        {summary.ReproductionCommand}
        ```
        """;

    private static void AddStat(StringBuilder html, string label, string value) =>
        html.AppendLine(CultureInfo.InvariantCulture, $"<div class=\"stat\"><div class=\"muted\">{Web(label)}</div><strong>{Web(value)}</strong></div>");

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, typeInfo, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteTextAsync(string path, string value, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, value, new UTF8Encoding(false), cancellationToken);

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        int index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
        return Math.Round(values[index], 3);
    }

    private static string ResolveTimestamp()
    {
        string? epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        return long.TryParse(epoch, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            : DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static string Web(string value) => WebUtility.HtmlEncode(value);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}

public sealed record AuditOptions(
    string CatalogPath,
    string OutputPath,
    int Width,
    int Height,
    long MaximumFileBytes,
    int PageSize,
    int EvaluationCorpusSize,
    bool UseTmdb,
    bool PersonalUseAcknowledged)
{
    public static AuditOptions Parse(IReadOnlyList<string> args)
    {
        string catalog = Get(args, "--catalog") ?? "data/catalog/recommendation_catalog.json";
        string output = Get(args, "--output") ?? "artifacts/poster-audit/p2";
        return new AuditOptions(
            catalog,
            output,
            ParseInt(Get(args, "--width"), 240, "--width"),
            ParseInt(Get(args, "--height"), 360, "--height"),
            ParseLong(Get(args, "--maximum-file-bytes"), 2_000_000, "--maximum-file-bytes"),
            ParseInt(Get(args, "--page-size"), 20, "--page-size"),
            ParseInt(Get(args, "--evaluation-size"), 100, "--evaluation-size"),
            Has(args, "--use-tmdb"),
            Has(args, "--acknowledge-personal-noncommercial-use"));
    }

    public string ReproductionCommand =>
        "dotnet run --project src/HorrorHarbor.Tools.PosterAudit -c Release -- " +
        $"--catalog {CatalogPath} --output {OutputPath}" +
        (UseTmdb ? " --use-tmdb --acknowledge-personal-noncommercial-use" : string.Empty);

    public void Validate()
    {
        if (!File.Exists(CatalogPath))
        {
            throw new ArgumentException($"Catalog does not exist: {CatalogPath}", nameof(CatalogPath));
        }

        if (Width is < 240 or > 1200 || Height is < 360 or > 1800 ||
            MaximumFileBytes is < 64_000 or > 10_000_000 || PageSize is < 5 or > 100 ||
            EvaluationCorpusSize is < 1 or > 500)
        {
            throw new ArgumentException("Poster audit options are outside supported safety bounds.");
        }

        if (UseTmdb && !PersonalUseAcknowledged)
        {
            throw new ArgumentException(
                "Live TMDB preparation requires --acknowledge-personal-noncommercial-use. " +
                "Do not use that switch for commercial, revenue-generating, or business use.");
        }

        if (!UseTmdb && PersonalUseAcknowledged)
        {
            throw new ArgumentException(
                "The personal-use acknowledgement is valid only together with --use-tmdb.");
        }
    }

    private static string? Get(IReadOnlyList<string> args, string name)
    {
        for (int index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count)
                {
                    throw new ArgumentException($"Missing value for {name}.", nameof(args));
                }

                return args[index + 1];
            }
        }

        return null;
    }

    private static bool Has(IReadOnlyList<string> args, string name) =>
        args.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

    private static int ParseInt(string? value, int fallback, string name) => value is null
        ? fallback
        : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"Invalid integer for {name}.", name);

    private static long ParseLong(string? value, long fallback, string name) => value is null
        ? fallback
        : long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : throw new ArgumentException($"Invalid integer for {name}.", name);
}
