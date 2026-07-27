namespace HorrorHarbor.Search.Contracts;

public static class SearchDocumentLimits
{
    public const int MaximumSearchableTextCodeUnits = 512;
    public const int MaximumTags = 32;
    public const int MaximumAliases = 32;
    public const int MaximumAvailabilityFacts = 32;
    public const int MaximumSourceIdentifierCodeUnits = 64;
    public const int MaximumSourceRevisionCodeUnits = 128;
}
