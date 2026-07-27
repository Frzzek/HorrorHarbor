namespace HorrorHarbor.Core.Abstractions;

public interface IPlexCredentialStore
{
    Task SaveTokenAsync(string connectionId, string token, CancellationToken cancellationToken = default);
    Task<string?> LoadTokenAsync(string connectionId, CancellationToken cancellationToken = default);
    Task DeleteTokenAsync(string connectionId, CancellationToken cancellationToken = default);
}

