using System.Net;

namespace HorrorHarbor.Data.Plex;

public interface IPlexHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default);
}
