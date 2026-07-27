using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;

namespace HorrorHarbor.Search.Ranking;

public enum SearchLexicalTier
{
    ExactCanonicalTitle = 0,
    ExactOriginalTitleOrAlias = 1,
    CanonicalTitlePhraseOrAllTokens = 2,
    CanonicalTitlePrefixOrToken = 3,
    ExactMetadataPhrase = 4,
    ExactTokenMatch = 5,
    FuzzyTitleMatch = 6,
    FuzzyNonTitleMatch = 7,
}

internal sealed class LexicalEvidenceCollection
{
    public LexicalEvidenceCollection(
        SearchLexicalTier? tier,
        ImmutableArray<SearchEvidence> evidence,
        int positiveHardFacetCount)
    {
        if (tier.HasValue && !Enum.IsDefined(tier.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tier),
                tier,
                "Unknown lexical tier.");
        }

        if (evidence.IsDefault)
        {
            throw new ArgumentException(
                "Lexical evidence must be initialized.",
                nameof(evidence));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(positiveHardFacetCount);

        Tier = tier;
        Evidence = evidence;
        PositiveHardFacetCount = positiveHardFacetCount;
    }

    public SearchLexicalTier? Tier { get; }

    public ImmutableArray<SearchEvidence> Evidence { get; }

    public int PositiveHardFacetCount { get; }
}
