namespace HorrorHarbor.Search.Ranking;

public sealed record LexicalScoreBreakdown
{
    internal LexicalScoreBreakdown(
        double titleIdentity,
        double lexicalCoverage,
        double fieldImportance,
        double exactness,
        double facetSatisfaction,
        double total)
    {
        TitleIdentity = Validate(titleIdentity, nameof(titleIdentity));
        LexicalCoverage = Validate(lexicalCoverage, nameof(lexicalCoverage));
        FieldImportance = Validate(fieldImportance, nameof(fieldImportance));
        Exactness = Validate(exactness, nameof(exactness));
        FacetSatisfaction = Validate(facetSatisfaction, nameof(facetSatisfaction));
        Total = Validate(total, nameof(total));
    }

    public double TitleIdentity { get; }

    public double LexicalCoverage { get; }

    public double FieldImportance { get; }

    public double Exactness { get; }

    public double FacetSatisfaction { get; }

    public double Total { get; }

    private static double Validate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Search score components must be finite and between 0 and 100.");
        }

        return value;
    }
}
