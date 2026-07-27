using System.Net;

namespace HorrorHarbor.Data.Plex;

public sealed class SystemPlexHostResolver : IPlexHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
