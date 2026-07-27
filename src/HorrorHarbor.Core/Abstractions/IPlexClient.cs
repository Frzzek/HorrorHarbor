using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Core.Abstractions;

public interface IPlexClient
{
    Task<PlexServerIdentity> GetIdentityAsync(
        PlexConnectionSettings settings,
        string token,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlexHistoryItem>> GetHistoryPageAsync(
        PlexConnectionSettings settings,
        string token,
        int start,
        int pageSize,
        DateTime? viewedAfterUtc,
        CancellationToken cancellationToken = default);
}

