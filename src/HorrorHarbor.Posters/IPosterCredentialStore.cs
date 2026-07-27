namespace HorrorHarbor.Posters;

public interface IPosterCredentialStore
{
    Task SaveTmdbTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<string?> LoadTmdbTokenAsync(CancellationToken cancellationToken = default);
    Task DeleteTmdbTokenAsync(CancellationToken cancellationToken = default);
}
