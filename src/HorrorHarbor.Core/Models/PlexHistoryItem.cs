namespace HorrorHarbor.Core.Models;

public sealed record PlexHistoryItem(
    string RatingKey,
    string MediaType,
    string Title,
    int? ReleaseYear,
    string? ProviderIdentifier,
    DateTime ViewedAtUtc,
    long? ViewOffsetMilliseconds,
    long? DurationMilliseconds);

