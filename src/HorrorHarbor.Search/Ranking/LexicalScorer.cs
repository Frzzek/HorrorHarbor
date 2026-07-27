using HorrorHarbor.Search.Contracts;

namespace HorrorHarbor.Search.Ranking;

internal static class LexicalScorer
{
    private const double TitleIdentityWeight = 0.30;
    private const double LexicalCoverageWeight = 0.25;
    private const double FieldImportanceWeight = 0.20;
    private const double ExactnessWeight = 0.20;
    private const double FacetSatisfactionWeight = 0.05;

    public static LexicalScoreBreakdown Score(
        LexicalEvidenceCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        double facetSatisfaction = 100;
        if (!collection.Tier.HasValue)
        {
            return new LexicalScoreBreakdown(
                titleIdentity: 0,
                lexicalCoverage: 0,
                fieldImportance: 0,
                exactness: 0,
                facetSatisfaction,
                total: 0);
        }

        double titleIdentity = GetTitleIdentity(collection.Evidence);
        double lexicalCoverage = 100;
        double fieldImportance = GetFieldImportance(collection.Evidence);
        double exactness = GetExactness(collection.Evidence);
        double total =
            (titleIdentity * TitleIdentityWeight) +
            (lexicalCoverage * LexicalCoverageWeight) +
            (fieldImportance * FieldImportanceWeight) +
            (exactness * ExactnessWeight) +
            (facetSatisfaction * FacetSatisfactionWeight);

        return new LexicalScoreBreakdown(
            titleIdentity,
            lexicalCoverage,
            fieldImportance,
            exactness,
            facetSatisfaction,
            total);
    }

    private static double GetTitleIdentity(
        IEnumerable<SearchEvidence> evidence)
    {
        double best = 0;
        foreach (SearchEvidence item in evidence)
        {
            best = Math.Max(best, item.Code switch
            {
                SearchEvidenceCode.CanonicalTitleExact => 100,
                SearchEvidenceCode.CanonicalTitleNormalizedExact => 98,
                SearchEvidenceCode.CanonicalTitlePunctuationAlias => 96,
                SearchEvidenceCode.OriginalTitleExact => 94,
                SearchEvidenceCode.RomanNumeralTitleAlias => 92,
                SearchEvidenceCode.ExplicitTitleAlias => 90,
                SearchEvidenceCode.CanonicalTitlePhrase => 86,
                SearchEvidenceCode.CanonicalTitleAllQueryTokens => 84,
                SearchEvidenceCode.CanonicalTitleToken => 76,
                SearchEvidenceCode.CanonicalTitlePrefix => 72,
                SearchEvidenceCode.FuzzyTitleEditDistance1 => 62,
                SearchEvidenceCode.FuzzyTitleEditDistance2 => 52,
                _ => 0,
            });
        }

        return best;
    }

    private static double GetFieldImportance(
        IEnumerable<SearchEvidence> evidence)
    {
        var facts = new Dictionary<LexicalFactKey, double>();
        foreach (SearchEvidence item in evidence)
        {
            if (item.NormalizedQueryValue is null ||
                item.NormalizedIndexedValue is null ||
                !TryGetFieldScore(item, out double score))
            {
                continue;
            }

            var key = new LexicalFactKey(
                item.Field,
                item.NormalizedQueryValue,
                item.NormalizedIndexedValue);
            if (!facts.TryGetValue(key, out double current) || score > current)
            {
                facts[key] = score;
            }
        }

        return facts.Count == 0 ? 0 : facts.Values.Average();
    }

    private static bool TryGetFieldScore(
        SearchEvidence evidence,
        out double score)
    {
        score = evidence.Field switch
        {
            SearchEvidenceField.CanonicalTitle => 100,
            SearchEvidenceField.OriginalTitle or
            SearchEvidenceField.TitleAlias => 90,
            SearchEvidenceField.Tag when evidence.Code is
                SearchEvidenceCode.TagExact or
                SearchEvidenceCode.TagExactNormalized => 80,
            SearchEvidenceField.PrimaryLens => 75,
            SearchEvidenceField.SecondaryLens => 70,
            SearchEvidenceField.Franchise => 65,
            SearchEvidenceField.Tag => 60,
            SearchEvidenceField.OriginalLanguage => 50,
            _ => 0,
        };
        return score > 0;
    }

    private static double GetExactness(IEnumerable<SearchEvidence> evidence)
    {
        var facts = new Dictionary<LexicalFactKey, double>();
        foreach (SearchEvidence item in evidence)
        {
            if (item.NormalizedQueryValue is null ||
                item.NormalizedIndexedValue is null ||
                !TryGetExactnessScore(item.Code, out double score))
            {
                continue;
            }

            var key = new LexicalFactKey(
                item.Field,
                item.NormalizedQueryValue,
                item.NormalizedIndexedValue);
            if (!facts.TryGetValue(key, out double current) || score < current)
            {
                facts[key] = score;
            }
        }

        return facts.Count == 0 ? 100 : facts.Values.Min();
    }

    private static bool TryGetExactnessScore(
        SearchEvidenceCode code,
        out double score)
    {
        score = code switch
        {
            SearchEvidenceCode.LexicalPhraseMatch or
            SearchEvidenceCode.LexicalTokenMatch or
            SearchEvidenceCode.TagExact or
            SearchEvidenceCode.TagExactNormalized or
            SearchEvidenceCode.TagTokenMatch or
            SearchEvidenceCode.CanonicalTitleExact or
            SearchEvidenceCode.CanonicalTitleNormalizedExact or
            SearchEvidenceCode.CanonicalTitlePunctuationAlias or
            SearchEvidenceCode.RomanNumeralTitleAlias or
            SearchEvidenceCode.OriginalTitleExact or
            SearchEvidenceCode.ExplicitTitleAlias or
            SearchEvidenceCode.CanonicalTitlePhrase or
            SearchEvidenceCode.CanonicalTitleAllQueryTokens or
            SearchEvidenceCode.CanonicalTitleToken => 100,
            SearchEvidenceCode.LexicalTokenPrefix or
            SearchEvidenceCode.CanonicalTitlePrefix => 85,
            SearchEvidenceCode.FuzzyTitleEditDistance1 or
            SearchEvidenceCode.FuzzyNonTitleEditDistance1 => 70,
            SearchEvidenceCode.FuzzyTitleEditDistance2 or
            SearchEvidenceCode.FuzzyNonTitleEditDistance2 => 50,
            _ => 0,
        };
        return score > 0;
    }

    private readonly record struct LexicalFactKey(
        SearchEvidenceField Field,
        string QueryValue,
        string IndexedValue);
}
