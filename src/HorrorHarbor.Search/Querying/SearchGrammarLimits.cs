namespace HorrorHarbor.Search.Querying;

public static class SearchGrammarLimits
{
    public const int MaximumRawQueryCodeUnits = 512;
    public const int MaximumTotalClauses = 32;
    public const int MaximumPositiveLexicalClauses = 16;
    public const int MaximumExclusionClauses = 16;
    public const int MaximumHardFacetClauses = 16;
    public const int MaximumQuotedPhrases = 8;
    public const int MaximumTokensPerPhrase = 12;
    public const int MaximumNormalizedTokenRunes = 64;
    public const int MaximumFuzzyEligibleTerms = 4;
    public const int MaximumLocalResults = 100;
    public const int MaximumOnlineResultsPerPage = 20;

    public const int EarliestReleaseYear = 1888;
    public const int LatestReleaseYear = 2200;
    public const int MinimumRuntimeMinutes = 1;
    public const int MaximumRuntimeMinutes = 1_000;
    public const int MinimumIntensity = 0;
    public const int MaximumIntensity = 5;
}
