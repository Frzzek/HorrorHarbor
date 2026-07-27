using System.Collections.Immutable;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Explanations;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Ranking;

namespace HorrorHarbor.Search.Contracts;

public sealed class SearchResult
{
    internal SearchResult(
        SearchIntent intent,
        RecommendationCandidate candidate,
        SearchLocalStatus localStatus,
        SearchLexicalTier? lexicalTier,
        LexicalScoreBreakdown lexicalScore,
        double affinityScore,
        double hybridScore,
        double qualityScore,
        double discoveryScore,
        ImmutableArray<SearchEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(lexicalScore);
        if (!Enum.IsDefined(localStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(localStatus),
                localStatus,
                "Unknown local status.");
        }

        if (evidence.IsDefault)
        {
            throw new ArgumentException(
                "Search result evidence must be initialized.",
                nameof(evidence));
        }

        Candidate = candidate;
        CandidateId = candidate.CandidateId;
        LocalStatus = localStatus;
        LexicalTier = lexicalTier;
        LexicalScore = new LexicalScoreBreakdown(
            Round(lexicalScore.TitleIdentity),
            Round(lexicalScore.LexicalCoverage),
            Round(lexicalScore.FieldImportance),
            Round(lexicalScore.Exactness),
            Round(lexicalScore.FacetSatisfaction),
            Round(lexicalScore.Total));
        AffinityScore = Round(affinityScore);
        HybridScore = Round(hybridScore);
        QualityScore = Round(qualityScore);
        DiscoveryScore = Round(discoveryScore);
        Evidence = evidence;
        Explanation = SearchExplanationBuilder.Build(intent, this);
    }

    public RecommendationCandidate Candidate { get; }

    public string CandidateId { get; }

    public SearchLocalStatus LocalStatus { get; }

    public SearchLexicalTier? LexicalTier { get; }

    public bool IsFilterOnly => !LexicalTier.HasValue;

    public LexicalScoreBreakdown LexicalScore { get; }

    public double AffinityScore { get; }

    public double HybridScore { get; }

    public double QualityScore { get; }

    public double DiscoveryScore { get; }

    public ImmutableArray<SearchEvidence> Evidence { get; }

    public SearchExplanation Explanation { get; }

    private static double Round(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
