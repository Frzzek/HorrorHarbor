using HorrorHarbor.Core.Abstractions;

namespace HorrorHarbor.App.Infrastructure;

public sealed class FallbackPlexCredentialStore(
    WindowsCredentialPlexCredentialStore primary,
    DpapiPlexCredentialStore fallback) : IPlexCredentialStore
{
    public async Task SaveTokenAsync(
        string connectionId,
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await primary.SaveTokenAsync(connectionId, token, cancellationToken).ConfigureAwait(false);
            await fallback.DeleteTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            await fallback.SaveTokenAsync(connectionId, token, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string?> LoadTokenAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? token = await primary.LoadTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
            return token ?? await fallback.LoadTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return await fallback.LoadTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteTokenAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await primary.DeleteTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
        }

        await fallback.DeleteTokenAsync(connectionId, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException;
}
