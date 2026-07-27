using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;

namespace HorrorHarbor.Data.Catalog;

public sealed class JsonCandidateCatalog : ICandidateCatalog
{
    private const long MaximumCatalogBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _catalogPath;

    public JsonCandidateCatalog(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        _catalogPath = Path.GetFullPath(catalogPath);
    }

    public async Task<IReadOnlyList<RecommendationCandidate>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(_catalogPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The HorrorHarbor recommendation catalog was not found.", _catalogPath);
        }

        if (file.Length is <= 0 or > MaximumCatalogBytes)
        {
            throw new InvalidDataException($"The recommendation catalog must be between 1 byte and {MaximumCatalogBytes:N0} bytes.");
        }

        List<RecommendationCandidate>? candidates;
        try
        {
            await using FileStream stream = new(
                _catalogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            candidates = await JsonSerializer.DeserializeAsync<List<RecommendationCandidate>>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The recommendation catalog is malformed or contains unsupported fields.", exception);
        }

        if (candidates is null || candidates.Count == 0)
        {
            throw new InvalidDataException("The recommendation catalog contains no candidates.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecommendationCandidate candidate in candidates)
        {
            try
            {
                RecommendationCandidateValidator.Validate(candidate);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Candidate '{candidate?.CandidateId ?? "<missing>"}' failed validation: {exception.Message}",
                    exception);
            }

            if (!ids.Add(candidate.CandidateId))
            {
                throw new InvalidDataException($"Duplicate candidate ID: {candidate.CandidateId}");
            }

            string identity = RecommendationCandidateValidator.IdentityKey(candidate);
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"Duplicate movie identity: {candidate.Title} ({candidate.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "unknown"})");
            }
        }

        return candidates.AsReadOnly();
    }
}
