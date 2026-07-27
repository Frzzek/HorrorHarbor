using System.Text.Json;

namespace HorrorHarbor.Posters;

public sealed class HybridPosterService(TmdbPosterService official, LocalPosterService fallback) : IPosterService
{
    public async Task<PosterAsset> GetPosterAsync(
        PosterDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await official.GetPosterAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return await fallback.GetPosterAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            InvalidDataException or
            HttpRequestException or
            JsonException or
            IOException)
        {
            return await fallback.GetPosterAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
    }
}
