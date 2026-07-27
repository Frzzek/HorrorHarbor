namespace HorrorHarbor.Core.Models;

public sealed record PlexSyncResult(
    int ReceivedCount,
    int ImportedCount,
    int DuplicateCount,
    DateTime? LatestViewedAtUtc);

