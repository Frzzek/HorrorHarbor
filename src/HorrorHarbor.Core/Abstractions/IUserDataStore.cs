using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Core.Abstractions;

public interface IUserDataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SavePlexServerAsync(string connectionId, PlexConnectionSettings settings, PlexServerIdentity identity, CancellationToken cancellationToken = default);
    Task<(PlexConnectionSettings Settings, PlexServerIdentity Identity, DateTime? LastSyncedUtc)?> LoadPlexServerAsync(string connectionId, CancellationToken cancellationToken = default);
    Task<PlexSyncResult> ImportPlexHistoryAsync(string connectionId, IReadOnlyList<PlexHistoryItem> items, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetPlexWatchedExclusionKeysAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlexHistoryItem>> GetPlexHistoryAsync(CancellationToken cancellationToken = default);
    Task RecordFeedbackAsync(string candidateId, RecommendationFeedback feedback, DateTime timestampUtc, CancellationToken cancellationToken = default);
    Task<RecommendationFeedback?> UndoLatestFeedbackAsync(string candidateId, CancellationToken cancellationToken = default);
    Task<bool> RestoreLatestFeedbackAsync(string candidateId, RecommendationFeedback feedback, CancellationToken cancellationToken = default);
    Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> LoadSettingAsync(string key, CancellationToken cancellationToken = default);
}
