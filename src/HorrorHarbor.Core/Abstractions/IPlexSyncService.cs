using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Core.Abstractions;

public interface IPlexSyncService
{
    Task<PlexServerIdentity> ConnectAsync(
        string connectionId,
        PlexConnectionSettings settings,
        string token,
        CancellationToken cancellationToken = default);

    Task<PlexSyncResult> SyncAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
