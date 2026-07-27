using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Explanations;

internal static class SearchExplanationBuilder
{
    private const double TitleIdentityWeight = 30;
    private const double LexicalCoverageWeight = 25;
    private const double FieldImportanceWeight = 20;
    private const double ExactnessWeight = 20;
    private const double FacetSatisfactionWeight = 5;
    private const double LexicalRankingWeight = 85;
    private const double AffinityRankingWeight = 15;

    public static SearchExplanation Build(
        SearchIntent intent,
        SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var rows = ImmutableArray.CreateBuilder<SearchExplanationRow>();
        bool hasPositiveFacet = false;
        foreach (SearchEvidence evidence in result.Evidence)
        {
            SearchExplanationRowKind kind = Classify(evidence.Code);
            hasPositiveFacet |= kind == SearchExplanationRowKind.FacetEvidence;
            rows.Add(new SearchExplanationRow(
                kind,
                evidence.StableCode,
                evidence: evidence));
        }

        AddScoreRows(rows, result, hasPositiveFacet);
        return new SearchExplanation(intent, result.LexicalTier, rows.ToImmutable());
    }

    private static void AddScoreRows(
        ImmutableArray<SearchExplanationRow>.Builder rows,
        SearchResult result,
        bool hasPositiveFacet)
    {
        if (!result.IsFilterOnly)
        {
            AddScore(rows, "score-title-identity", result.LexicalScore.TitleIdentity, TitleIdentityWeight);
            AddScore(rows, "score-lexical-coverage", result.LexicalScore.LexicalCoverage, LexicalCoverageWeight);
            AddScore(rows, "score-field-importance", result.LexicalScore.FieldImportance, FieldImportanceWeight);
            AddScore(rows, "score-exactness", result.LexicalScore.Exactness, ExactnessWeight);
            if (hasPositiveFacet)
            {
                AddScore(rows, "score-facet-satisfaction", result.LexicalScore.FacetSatisfaction, FacetSatisfactionWeight);
            }

            AddScore(rows, "score-lexical-total", result.LexicalScore.Total, LexicalRankingWeight);
            AddRankingScore(rows, "score-affinity", result.AffinityScore, AffinityRankingWeight);
        }
        else
        {
            if (hasPositiveFacet)
            {
                AddScore(rows, "score-facet-satisfaction", result.LexicalScore.FacetSatisfaction, FacetSatisfactionWeight);
            }

            AddRankingScore(rows, "score-affinity", result.AffinityScore, 100);
        }

        AddRankingScore(rows, "score-hybrid-total", result.HybridScore);
        AddRankingScore(rows, "score-quality-tie-break", result.QualityScore);
        AddRankingScore(rows, "score-discovery-tie-break", result.DiscoveryScore);
    }

    private static void AddScore(
        ImmutableArray<SearchExplanationRow>.Builder rows,
        string stableCode,
        double value,
        double weight) => rows.Add(new SearchExplanationRow(
            SearchExplanationRowKind.LexicalScore,
            stableCode,
            value,
            weight));

    private static void AddRankingScore(
        ImmutableArray<SearchExplanationRow>.Builder rows,
        string stableCode,
        double value,
        double? weight = null) => rows.Add(new SearchExplanationRow(
            SearchExplanationRowKind.RankingScore,
            stableCode,
            value,
            weight));

    private static SearchExplanationRowKind Classify(
        SearchEvidenceCode code) => code switch
        {
            SearchEvidenceCode.YearFilterSatisfied or
            SearchEvidenceCode.RuntimeFilterSatisfied or
            SearchEvidenceCode.ScareFilterSatisfied or
            SearchEvidenceCode.GoreFilterSatisfied or
            SearchEvidenceCode.DreadFilterSatisfied or
            SearchEvidenceCode.SlasherFilterSatisfied or
            SearchEvidenceCode.ExtremeFilterSatisfied or
            SearchEvidenceCode.AvailabilityUnknown or
            SearchEvidenceCode.AvailabilityVerified or
            SearchEvidenceCode.AvailabilityProviderMatch =>
                SearchExplanationRowKind.FacetEvidence,
            SearchEvidenceCode.StatusUnseen or
            SearchEvidenceCode.StatusWatched or
            SearchEvidenceCode.StatusInterested or
            SearchEvidenceCode.StatusNotNow or
            SearchEvidenceCode.StatusDisliked =>
                SearchExplanationRowKind.StatusEvidence,
            SearchEvidenceCode.ExclusionSatisfied =>
                SearchExplanationRowKind.ExclusionEvidence,
            _ => SearchExplanationRowKind.MatchEvidence,
        };
}
