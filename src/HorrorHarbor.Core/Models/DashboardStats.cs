namespace HorrorHarbor.Core.Models;

public sealed record DashboardStats(
    int RawEvents,
    int CanonicalTitles,
    int RelevantTitles,
    int CoreHorrorTitles,
    int MovieIdentities,
    int TvIdentities,
    int NeedsReviewTitles,
    int FlaggedEvents)
{
    public static DashboardStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}
