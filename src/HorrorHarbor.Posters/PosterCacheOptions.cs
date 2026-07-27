namespace HorrorHarbor.Posters;

public sealed record PosterCacheOptions(
    string RootDirectory,
    int PixelWidth = 480,
    int PixelHeight = 720,
    long MaximumFileBytes = 2_000_000)
{
    public static PosterCacheOptions CreateDefault()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new PosterCacheOptions(Path.Combine(localData, "NocturneHarbor", "posters-v1"));
    }
}
