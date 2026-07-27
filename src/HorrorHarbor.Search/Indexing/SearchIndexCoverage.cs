namespace HorrorHarbor.Search.Indexing;

public sealed class SearchIndexCoverage
{
    internal SearchIndexCoverage(
        int releaseYear,
        int runtime,
        int primaryLens,
        int secondaryLens,
        int franchise,
        int originalTitle,
        int originalLanguage,
        int verifiedAvailability,
        int slasher,
        int extreme)
    {
        ReleaseYear = releaseYear;
        Runtime = runtime;
        PrimaryLens = primaryLens;
        SecondaryLens = secondaryLens;
        Franchise = franchise;
        OriginalTitle = originalTitle;
        OriginalLanguage = originalLanguage;
        VerifiedAvailability = verifiedAvailability;
        Slasher = slasher;
        Extreme = extreme;
    }

    public int ReleaseYear { get; }

    public int Runtime { get; }

    public int PrimaryLens { get; }

    public int SecondaryLens { get; }

    public int Franchise { get; }

    public int OriginalTitle { get; }

    public int OriginalLanguage { get; }

    public int VerifiedAvailability { get; }

    public int Slasher { get; }

    public int Extreme { get; }
}
