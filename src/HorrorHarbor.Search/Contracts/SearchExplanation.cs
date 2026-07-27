using System.Collections.Immutable;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Ranking;

namespace HorrorHarbor.Search.Contracts;

public enum SearchExplanationRowKind
{
    MatchEvidence,
    FacetEvidence,
    StatusEvidence,
    ExclusionEvidence,
    LexicalScore,
    RankingScore,
}

public sealed record SearchExplanationRow
{
    internal SearchExplanationRow(
        SearchExplanationRowKind kind,
        string stableCode,
        double? value = null,
        double? weight = null,
        SearchEvidence? evidence = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown search explanation row kind.");
        }

        ArgumentException.ThrowIfNullOrEmpty(stableCode);
        ValidatePercentage(value, nameof(value));
        ValidatePercentage(weight, nameof(weight));
        if ((kind is SearchExplanationRowKind.MatchEvidence or
                SearchExplanationRowKind.FacetEvidence or
                SearchExplanationRowKind.StatusEvidence or
                SearchExplanationRowKind.ExclusionEvidence) !=
            (evidence is not null))
        {
            throw new ArgumentException(
                "Evidence rows require evidence and score rows cannot contain it.",
                nameof(evidence));
        }

        Kind = kind;
        StableCode = stableCode;
        Value = value;
        Weight = weight;
        Evidence = evidence;
    }

    public SearchExplanationRowKind Kind { get; }

    public string StableCode { get; }

    public double? Value { get; }

    public double? Weight { get; }

    public SearchEvidence? Evidence { get; }

    private static void ValidatePercentage(double? value, string parameterName)
    {
        if (value.HasValue &&
            (!double.IsFinite(value.Value) || value.Value is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Explanation values must be finite percentages between 0 and 100.");
        }
    }
}

public sealed record SearchExplanation
{
    internal SearchExplanation(
        SearchIntent intent,
        SearchLexicalTier? strongestMatchClass,
        ImmutableArray<SearchExplanationRow> rows)
    {
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intent),
                intent,
                "Unknown search intent.");
        }

        if (strongestMatchClass.HasValue &&
            !Enum.IsDefined(strongestMatchClass.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(strongestMatchClass),
                strongestMatchClass,
                "Unknown lexical match class.");
        }

        if (rows.IsDefault)
        {
            throw new ArgumentException(
                "Search explanation rows must be initialized.",
                nameof(rows));
        }

        Intent = intent;
        StrongestMatchClass = strongestMatchClass;
        Rows = rows;
    }

    public SearchIntent Intent { get; }

    public SearchLexicalTier? StrongestMatchClass { get; }

    public bool IsFilterOnly => !StrongestMatchClass.HasValue;

    public ImmutableArray<SearchExplanationRow> Rows { get; }
}
