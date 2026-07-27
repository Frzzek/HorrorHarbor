namespace HorrorHarbor.Posters;

public interface IPosterService
{
    Task<PosterAsset> GetPosterAsync(
        PosterDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
