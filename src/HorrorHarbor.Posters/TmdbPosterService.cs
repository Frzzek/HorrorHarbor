using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HorrorHarbor.Posters;

public sealed class TmdbPosterService(
    HttpClient httpClient,
    IPosterCredentialStore credentialStore,
    TmdbPosterOptions options) : IPosterService, IDisposable
{
    private static readonly Uri ApiRoot = new("https://api.themoviedb.org/3/");
    private static readonly Uri ImageRoot = new("https://image.tmdb.org/t/p/w500/");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<PosterAsset> GetPosterAsync(
        PosterDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateOptions(options);
        ValidateDescriptor(descriptor);
        string? token = await credentialStore.LoadTmdbTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TMDB artwork is not configured.");
        }

        string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"tmdb-v1|{descriptor.TmdbId?.ToString(CultureInfo.InvariantCulture)}|{descriptor.Title}|{descriptor.ReleaseYear}"))).ToLowerInvariant();
        string rootDirectory = Path.GetFullPath(options.RootDirectory);
        Directory.CreateDirectory(rootDirectory);
        string metadataPath = Path.Combine(rootDirectory, cacheKey + ".json");
        SemaphoreSlim gate = _locks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PosterAsset? cached = await TryLoadCacheAsync(rootDirectory, metadataPath, cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }

            TmdbMatch? match = await FindMatchAsync(descriptor, token, cancellationToken).ConfigureAwait(false);
            if (match is null)
            {
                throw new InvalidDataException("TMDB did not return one unambiguous title and year match with poster artwork.");
            }

            byte[] encoded = await DownloadImageAsync(match.Movie.PosterPath!, cancellationToken).ConfigureAwait(false);
            ValidatedImage validated = EncodedImageValidator.Validate(
                encoded,
                options.MaximumImageBytes,
                options.MaximumPixelWidth,
                options.MaximumPixelHeight);
            string imagePath = Path.Combine(rootDirectory, cacheKey + validated.Extension);
            string sha256 = Convert.ToHexString(SHA256.HashData(encoded));
            var provenance = new PosterProvenance(
                PosterSourceKind.TmdbOfficial,
                "TMDB",
                match.Movie.PosterPath,
                match.Movie.Id.ToString(CultureInfo.InvariantCulture),
                match.Method,
                match.Confidence,
                match.Movie.OriginalTitle,
                match.Movie.OriginalLanguage,
                "This product uses the TMDB API but is not endorsed or certified by TMDB.",
                "https://www.themoviedb.org/api-terms-of-use",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sha256);
            await WriteAtomicallyAsync(imagePath, encoded, cancellationToken).ConfigureAwait(false);
            byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(provenance, PosterJsonContext.Default.PosterProvenance);
            await WriteAtomicallyAsync(metadataPath, metadata, cancellationToken).ConfigureAwait(false);
            return new PosterAsset(imagePath, validated.PixelWidth, validated.PixelHeight, false, provenance);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim gate in _locks.Values)
        {
            gate.Dispose();
        }

        _locks.Clear();
    }

    private async Task<TmdbMatch?> FindMatchAsync(
        PosterDescriptor descriptor,
        string token,
        CancellationToken cancellationToken)
    {
        if (descriptor.TmdbId is int explicitId)
        {
            var directUri = new Uri(ApiRoot, "movie/" + explicitId.ToString(CultureInfo.InvariantCulture));
            byte[] directResponse = await SendBoundedAsync(
                directUri,
                token,
                options.MaximumSearchBytes,
                "application/json",
                cancellationToken).ConfigureAwait(false);
            TmdbMovieResult movie = JsonSerializer.Deserialize(directResponse, PosterJsonContext.Default.TmdbMovieResult)
                ?? throw new InvalidDataException("TMDB title lookup returned an empty response.");
            return string.IsNullOrWhiteSpace(movie.PosterPath) || movie.Id != explicitId
                ? null
                : new TmdbMatch(movie, 1, "catalog-tmdb-id");
        }

        string query = Uri.EscapeDataString(descriptor.Title);
        string year = descriptor.ReleaseYear is null
            ? string.Empty
            : "&year=" + descriptor.ReleaseYear.Value.ToString(CultureInfo.InvariantCulture);
        var uri = new Uri(ApiRoot, $"search/movie?query={query}{year}&include_adult=false&language=en-US&page=1");
        byte[] response = await SendBoundedAsync(uri, token, options.MaximumSearchBytes, "application/json", cancellationToken).ConfigureAwait(false);
        TmdbSearchResponse payload = JsonSerializer.Deserialize(response, PosterJsonContext.Default.TmdbSearchResponse)
            ?? throw new InvalidDataException("TMDB search returned an empty response.");
        return TmdbIdentityMatcher.Select(descriptor, payload.Results ?? []);
    }

    private async Task<byte[]> DownloadImageAsync(string posterPath, CancellationToken cancellationToken)
    {
        if (posterPath.Length is < 2 or > 200 || posterPath[0] != '/' ||
            posterPath.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '/' or '_' or '-' or '.')))
        {
            throw new InvalidDataException("TMDB returned an invalid poster path.");
        }

        var uri = new Uri(ImageRoot, posterPath.TrimStart('/'));
        return await SendBoundedAsync(uri, null, options.MaximumImageBytes, "image/", cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> SendBoundedAsync(
        Uri uri,
        string? token,
        long maximumBytes,
        string expectedMediaTypePrefix,
        CancellationToken cancellationToken)
    {
        ValidateDestination(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(expectedMediaTypePrefix == "image/" ? "image/jpeg" : "application/json"));
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new HttpRequestException("TMDB redirects are not accepted.", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new InvalidDataException("TMDB response exceeded the encoded size limit.");
        }

        string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.StartsWith(expectedMediaTypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("TMDB response had an unexpected media type.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException("TMDB response exceeded the encoded size limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private async Task<PosterAsset?> TryLoadCacheAsync(
        string rootDirectory,
        string metadataPath,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(metadataPath) > TimeSpan.FromDays(options.CacheDays))
        {
            DeleteCacheFiles(rootDirectory, metadataPath, cacheKey);
            return null;
        }

        try
        {
            PosterProvenance? provenance = JsonSerializer.Deserialize(
                await File.ReadAllBytesAsync(metadataPath, cancellationToken).ConfigureAwait(false),
                PosterJsonContext.Default.PosterProvenance);
            if (provenance is null || provenance.SourceKind != PosterSourceKind.TmdbOfficial)
            {
                return null;
            }

            foreach (string extension in new[] { ".jpg", ".png" })
            {
                string path = Path.Combine(rootDirectory, cacheKey + extension);
                if (!File.Exists(path))
                {
                    continue;
                }

                byte[] encoded = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                ValidatedImage image = EncodedImageValidator.Validate(
                    encoded,
                    options.MaximumImageBytes,
                    options.MaximumPixelWidth,
                    options.MaximumPixelHeight);
                if (string.Equals(Convert.ToHexString(SHA256.HashData(encoded)), provenance.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new PosterAsset(path, image.PixelWidth, image.PixelHeight, false, provenance);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
        }

        DeleteCacheFiles(rootDirectory, metadataPath, cacheKey);
        return null;
    }

    private static void DeleteCacheFiles(string rootDirectory, string metadataPath, string cacheKey)
    {
        TryDelete(metadataPath);
        TryDelete(Path.Combine(rootDirectory, cacheKey + ".jpg"));
        TryDelete(Path.Combine(rootDirectory, cacheKey + ".png"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] data, CancellationToken cancellationToken)
    {
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, data, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void ValidateDestination(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            uri.UserInfo.Length != 0 ||
            uri.Host is not ("api.themoviedb.org" or "image.tmdb.org"))
        {
            throw new InvalidDataException("TMDB destination failed the HTTPS host allowlist.");
        }
    }

    private static void ValidateOptions(TmdbPosterOptions value)
    {
        if (string.IsNullOrWhiteSpace(value.RootDirectory) ||
            value.MaximumSearchBytes is < 16_000 or > 5_000_000 ||
            value.MaximumImageBytes is < 64_000 or > 20_000_000 ||
            value.MaximumPixelWidth is < 240 or > 4000 ||
            value.MaximumPixelHeight is < 360 or > 6000 ||
            value.CacheDays is < 1 or > 365)
        {
            throw new ArgumentException("TMDB poster options are outside supported safety bounds.", nameof(value));
        }
    }

    private static void ValidateDescriptor(PosterDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Title) || descriptor.Title.Length > 300 ||
            descriptor.TmdbId is <= 0 ||
            descriptor.OriginalTitle?.Length > 300 ||
            descriptor.OriginalLanguage?.Length > 20)
        {
            throw new ArgumentException("Poster identity is outside supported bounds.", nameof(descriptor));
        }
    }
}
