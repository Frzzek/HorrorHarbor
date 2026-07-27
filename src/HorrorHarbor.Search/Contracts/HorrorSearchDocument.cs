using System.Collections.Immutable;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;
using HorrorHarbor.Search.Validation;

namespace HorrorHarbor.Search.Contracts;

public enum SearchAvailabilityState
{
    Unknown,
    Verified,
}

public sealed class HorrorSearchDocument
{
    public HorrorSearchDocument(
        RecommendationCandidate candidate,
        string sourceRevision,
        SearchAvailabilityState availabilityState,
        IEnumerable<SearchAlias>? aliases = null,
        IEnumerable<SearchAvailabilityFact>? availabilityFacts = null)
    {
        if (!Enum.IsDefined(availabilityState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(availabilityState),
                availabilityState,
                "Unknown search availability state.");
        }

        Candidate = CreateCandidateSnapshot(candidate);
        SourceRevision = SearchDocumentValueValidator.ValidateStableKey(
            sourceRevision,
            nameof(sourceRevision),
            SearchDocumentLimits.MaximumSourceRevisionCodeUnits);
        Aliases = MaterializeAliases(aliases);
        AvailabilityFacts = MaterializeAvailabilityFacts(availabilityFacts);
        ValidateAvailabilityState(availabilityState, AvailabilityFacts.Length);
        AvailabilityState = availabilityState;
    }

    public RecommendationCandidate Candidate { get; }

    public string CandidateId => Candidate.CandidateId;

    public string SourceRevision { get; }

    public SearchAvailabilityState AvailabilityState { get; }

    public ImmutableArray<SearchAlias> Aliases { get; }

    public ImmutableArray<SearchAvailabilityFact> AvailabilityFacts { get; }

    private static RecommendationCandidate CreateCandidateSnapshot(
        RecommendationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        string[] copiedTags = CopyTags(candidate.Tags);
        RecommendationCandidate snapshot = candidate with
        {
            Tags = Array.AsReadOnly(copiedTags),
        };

        RecommendationCandidateValidator.Validate(snapshot, nameof(candidate));
        SearchDocumentValidator.ValidateCandidateFields(snapshot);

        string[] normalizedTags =
            SearchDocumentValidator.NormalizeAndDeduplicateTags(copiedTags);

        return snapshot with
        {
            Tags = Array.AsReadOnly(normalizedTags),
        };
    }

    private static string[] CopyTags(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            throw new ArgumentException("Tags cannot be null.", nameof(tags));
        }

        if (tags.Count > SearchDocumentLimits.MaximumTags)
        {
            throw new ArgumentException(
                $"A search document cannot contain more than {SearchDocumentLimits.MaximumTags} tags.",
                nameof(tags));
        }

        var copy = new string[tags.Count];
        for (int index = 0; index < tags.Count; index++)
        {
            copy[index] = tags[index];
        }

        return copy;
    }

    private static ImmutableArray<SearchAlias> MaterializeAliases(
        IEnumerable<SearchAlias>? aliases)
    {
        if (aliases is null)
        {
            return [];
        }

        var values = new List<SearchAlias>();
        foreach (SearchAlias? alias in aliases)
        {
            if (alias is null)
            {
                throw new ArgumentException(
                    "Aliases cannot contain null values.",
                    nameof(aliases));
            }

            if (values.Count == SearchDocumentLimits.MaximumAliases)
            {
                throw new ArgumentException(
                    $"A search document cannot contain more than {SearchDocumentLimits.MaximumAliases} aliases.",
                    nameof(aliases));
            }

            values.Add(alias.Copy());
        }

        values.Sort(CompareAliases);
        var result = ImmutableArray.CreateBuilder<SearchAlias>(values.Count);
        string? previousNormalizedValue = null;
        foreach (SearchAlias alias in values)
        {
            if (string.Equals(
                previousNormalizedValue,
                alias.NormalizedValue,
                StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(alias);
            previousNormalizedValue = alias.NormalizedValue;
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<SearchAvailabilityFact> MaterializeAvailabilityFacts(
        IEnumerable<SearchAvailabilityFact>? availabilityFacts)
    {
        if (availabilityFacts is null)
        {
            return [];
        }

        var values = new List<SearchAvailabilityFact>();
        foreach (SearchAvailabilityFact? fact in availabilityFacts)
        {
            if (fact is null)
            {
                throw new ArgumentException(
                    "Availability facts cannot contain null values.",
                    nameof(availabilityFacts));
            }

            if (values.Count == SearchDocumentLimits.MaximumAvailabilityFacts)
            {
                throw new ArgumentException(
                    $"A search document cannot contain more than {SearchDocumentLimits.MaximumAvailabilityFacts} availability facts.",
                    nameof(availabilityFacts));
            }

            values.Add(fact.Copy());
        }

        values.Sort(CompareAvailabilityFacts);
        var result = ImmutableArray.CreateBuilder<SearchAvailabilityFact>(values.Count);
        SearchAvailabilityFact? previous = null;
        foreach (SearchAvailabilityFact fact in values)
        {
            if (previous is not null && SameAvailabilityIdentity(previous, fact))
            {
                continue;
            }

            result.Add(fact);
            previous = fact;
        }

        return result.ToImmutable();
    }

    private static int CompareAliases(SearchAlias left, SearchAlias right)
    {
        int result = StringComparer.Ordinal.Compare(
            left.NormalizedValue,
            right.NormalizedValue);
        if (result != 0)
        {
            return result;
        }

        result = left.Provenance.SourceKind.CompareTo(right.Provenance.SourceKind);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.Value, right.Value);
        if (result != 0)
        {
            return result;
        }

        return CompareProvenance(left.Provenance, right.Provenance);
    }

    private static int CompareAvailabilityFacts(
        SearchAvailabilityFact left,
        SearchAvailabilityFact right)
    {
        int result = StringComparer.Ordinal.Compare(
            left.NormalizedProviderValue,
            right.NormalizedProviderValue);
        if (result != 0)
        {
            return result;
        }

        result = CompareProvenance(left.Provenance, right.Provenance);
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(
                left.ProviderValue,
                right.ProviderValue);
    }

    private static int CompareProvenance(
        SearchMetadataProvenance left,
        SearchMetadataProvenance right)
    {
        int result = left.SourceKind.CompareTo(right.SourceKind);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(
            left.SourceIdentifier,
            right.SourceIdentifier);
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(
                left.SourceRevision,
                right.SourceRevision);
    }

    private static bool SameAvailabilityIdentity(
        SearchAvailabilityFact left,
        SearchAvailabilityFact right) =>
        string.Equals(
            left.NormalizedProviderValue,
            right.NormalizedProviderValue,
            StringComparison.Ordinal) &&
        CompareProvenance(left.Provenance, right.Provenance) == 0;

    private static void ValidateAvailabilityState(
        SearchAvailabilityState state,
        int factCount)
    {
        if (state == SearchAvailabilityState.Unknown && factCount != 0)
        {
            throw new ArgumentException(
                "Unknown availability cannot contain verified facts.",
                nameof(state));
        }

        if (state == SearchAvailabilityState.Verified && factCount == 0)
        {
            throw new ArgumentException(
                "Verified availability requires at least one fact.",
                nameof(state));
        }
    }
}
