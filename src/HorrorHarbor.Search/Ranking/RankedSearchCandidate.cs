using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;

namespace HorrorHarbor.Search.Ranking;

internal sealed class RankedSearchCandidate
{
    public RankedSearchCandidate(
        int documentId,
        string candidateId,
        string normalizedTitle,
        int? releaseYear,
        SearchLexicalTier? lexicalTier,
        LexicalScoreBreakdown lexicalScore,
        double affinityScore,
        double hybridScore,
        double qualityScore,
        double discoveryScore,
        ImmutableArray<SearchEvidence> evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        ArgumentException.ThrowIfNullOrEmpty(candidateId);
        ArgumentException.ThrowIfNullOrEmpty(normalizedTitle);
        ArgumentNullException.ThrowIfNull(lexicalScore);
        if (lexicalTier.HasValue && !Enum.IsDefined(lexicalTier.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lexicalTier),
                lexicalTier,
                "Unknown lexical tier.");
        }

        if (evidence.IsDefault)
        {
            throw new ArgumentException(
                "Ranked evidence must be initialized.",
                nameof(evidence));
        }

        DocumentId = documentId;
        CandidateId = candidateId;
        NormalizedTitle = normalizedTitle;
        ReleaseYear = releaseYear;
        LexicalTier = lexicalTier;
        LexicalScore = lexicalScore;
        AffinityScore = ValidateScore(affinityScore, nameof(affinityScore));
        HybridScore = ValidateScore(hybridScore, nameof(hybridScore));
        QualityScore = ValidateScore(qualityScore, nameof(qualityScore));
        DiscoveryScore = ValidateScore(discoveryScore, nameof(discoveryScore));
        Evidence = evidence;
    }

    public int DocumentId { get; }

    public string CandidateId { get; }

    public string NormalizedTitle { get; }

    public int? ReleaseYear { get; }

    public SearchLexicalTier? LexicalTier { get; }

    public LexicalScoreBreakdown LexicalScore { get; }

    public double AffinityScore { get; }

    public double HybridScore { get; }

    public double QualityScore { get; }

    public double DiscoveryScore { get; }

    public ImmutableArray<SearchEvidence> Evidence { get; }

    private static double ValidateScore(double score, string parameterName)
    {
        if (!double.IsFinite(score) || score is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                score,
                "Ranked scores must be finite and between 0 and 100.");
        }

        return score;
    }
}
