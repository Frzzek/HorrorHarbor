namespace HorrorHarbor.Core.Models;

public sealed record TitleCatalogItem(
    string TitleId,
    string MediaType,
    string DisplayTitle,
    int? ReleaseYear,
    string PlexGuid,
    string WatchExclusionKey,
    int RawEventCount,
    double WeightedTasteEventCount,
    DateTime FirstWatchedUtc,
    DateTime LastWatchedUtc,
    string ScopeTier,
    string? PrimaryLens,
    string? SecondaryLens,
    string ClassificationConfidence,
    string ClassificationStatus,
    bool NeedsReview);
