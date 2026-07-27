namespace HorrorHarbor.Posters;

public sealed record TmdbPosterOptions(
    string RootDirectory,
    long MaximumSearchBytes = 1_000_000,
    long MaximumImageBytes = 8_000_000,
    int MaximumPixelWidth = 2000,
    int MaximumPixelHeight = 3000,
    int CacheDays = 30)
{
    public static TmdbPosterOptions CreateDefault()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new TmdbPosterOptions(Path.Combine(local, "NocturneHarbor", "official-posters-v1"));
    }
}
