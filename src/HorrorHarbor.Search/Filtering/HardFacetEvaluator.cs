using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Filtering;

internal static class HardFacetEvaluator
{
    public static int[] Apply(
        SearchIndexSnapshot snapshot,
        ReadOnlySpan<int> candidateIds,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        CancellationToken cancellationToken)
    {
        var result = new List<int>(candidateIds.Length);
        for (int index = 0; index < candidateIds.Length; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int documentId = candidateIds[index];
            if (MatchesAllPositiveFacets(
                snapshot.GetDocument(documentId),
                query,
                userContext))
            {
                result.Add(documentId);
            }
        }

        return [.. result];
    }

    internal static bool Matches(
        HorrorSearchDocument document,
        SearchQueryClause clause,
        SearchUserContext userContext) => clause switch
        {
            SearchNumericClause numeric => MatchesNumeric(
                document.Candidate,
                numeric),
            SearchBooleanClause boolean => MatchesBoolean(
                document.Candidate,
                boolean),
            SearchAvailabilityClause availability => MatchesAvailability(
                document,
                availability),
            SearchStatusClause status =>
                userContext.GetStatusUnchecked(document.CandidateId) == status.Value,
            _ => throw new InvalidOperationException(
                "Hard facet evaluation received an unsupported clause."),
        };

    private static bool MatchesAllPositiveFacets(
        HorrorSearchDocument document,
        ParsedSearchQuery query,
        SearchUserContext userContext)
    {
        foreach (SearchQueryClause clause in query.Clauses)
        {
            if (clause.Occurrence == SearchClauseOccurrence.Positive &&
                clause is not SearchLexicalClause &&
                !Matches(document, clause, userContext))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesNumeric(
        RecommendationCandidate candidate,
        SearchNumericClause clause)
    {
        int? value = clause.Field switch
        {
            SearchField.Year => candidate.ReleaseYear,
            SearchField.Runtime => candidate.RuntimeMinutes,
            SearchField.Scare => candidate.ScareIntensity,
            SearchField.Gore => candidate.GoreIntensity,
            SearchField.Dread => candidate.DreadIntensity,
            _ => throw new InvalidOperationException(
                "Numeric clause has an unsupported field."),
        };

        if (!value.HasValue)
        {
            return false;
        }

        return clause.Operator switch
        {
            SearchNumericOperator.Equal => value.Value == clause.FirstValue,
            SearchNumericOperator.LessThan => value.Value < clause.FirstValue,
            SearchNumericOperator.LessThanOrEqual =>
                value.Value <= clause.FirstValue,
            SearchNumericOperator.GreaterThan => value.Value > clause.FirstValue,
            SearchNumericOperator.GreaterThanOrEqual =>
                value.Value >= clause.FirstValue,
            SearchNumericOperator.InclusiveRange =>
                value.Value >= clause.FirstValue &&
                value.Value <= clause.SecondValue,
            _ => throw new InvalidOperationException(
                "Unknown numeric search operator."),
        };
    }

    private static bool MatchesBoolean(
        RecommendationCandidate candidate,
        SearchBooleanClause clause)
    {
        bool value = clause.Field switch
        {
            SearchField.Slasher => candidate.IsSlasher,
            SearchField.Extreme => candidate.IsExtreme,
            _ => throw new InvalidOperationException(
                "Boolean clause has an unsupported field."),
        };

        return value == clause.Value;
    }

    private static bool MatchesAvailability(
        HorrorSearchDocument document,
        SearchAvailabilityClause clause) => clause.Kind switch
        {
            SearchAvailabilityKind.Unknown =>
                document.AvailabilityState == SearchAvailabilityState.Unknown,
            SearchAvailabilityKind.Verified =>
                document.AvailabilityState == SearchAvailabilityState.Verified,
            SearchAvailabilityKind.ProviderDefined =>
                document.AvailabilityFacts.Any(fact => string.Equals(
                    fact.NormalizedProviderValue,
                    clause.ProviderValue,
                    StringComparison.Ordinal)),
            _ => throw new InvalidOperationException(
                "Unknown availability kind."),
        };
}
