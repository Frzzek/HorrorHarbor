using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;

namespace HorrorHarbor.App.Infrastructure;

public sealed class PlexSyncService(
    IPlexClient client,
    IPlexCredentialStore credentialStore,
    IUserDataStore userDataStore) : IPlexSyncService
{
    private const int PageSize = 200;
    private const int MaximumItemsPerSync = 100_000;

    public async Task<PlexServerIdentity> ConnectAsync(
        string connectionId,
        PlexConnectionSettings settings,
        string token,
        CancellationToken cancellationToken = default)
    {
        PlexServerIdentity identity = await client.GetIdentityAsync(settings, token, cancellationToken).ConfigureAwait(false);
        string? previousToken = await credentialStore.LoadTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
        await credentialStore.SaveTokenAsync(connectionId, token, cancellationToken).ConfigureAwait(false);
        try
        {
            await userDataStore.SavePlexServerAsync(connectionId, settings, identity, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (previousToken is null)
            {
                await credentialStore.DeleteTokenAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await credentialStore.SaveTokenAsync(connectionId, previousToken, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        return identity;
    }

    public async Task<PlexSyncResult> SyncAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var server = await userDataStore.LoadPlexServerAsync(connectionId, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Connect to Plex before syncing history.");
        string token = await credentialStore.LoadTokenAsync(connectionId, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("The saved Plex credential is unavailable.");

        PlexServerIdentity currentIdentity = await client.GetIdentityAsync(
            server.Settings, token, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                currentIdentity.MachineIdentifier,
                server.Identity.MachineIdentifier,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Plex server identity changed. Reconnect explicitly before importing history.");
        }

        var received = 0;
        var imported = 0;
        var duplicates = 0;
        DateTime? latest = null;
        for (var start = 0; start < MaximumItemsPerSync; start += PageSize)
        {
            IReadOnlyList<PlexHistoryItem> page = await client.GetHistoryPageAsync(
                server.Settings,
                token,
                start,
                PageSize,
                server.LastSyncedUtc,
                cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            PlexSyncResult result = await userDataStore.ImportPlexHistoryAsync(connectionId, page, cancellationToken).ConfigureAwait(false);
            received += result.ReceivedCount;
            imported += result.ImportedCount;
            duplicates += result.DuplicateCount;
            latest = latest is null || result.LatestViewedAtUtc > latest ? result.LatestViewedAtUtc : latest;
            if (page.Count < PageSize)
            {
                break;
            }
        }

        return new PlexSyncResult(received, imported, duplicates, latest);
    }
}
