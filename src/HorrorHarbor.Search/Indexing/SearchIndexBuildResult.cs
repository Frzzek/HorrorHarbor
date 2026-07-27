using System.Collections.Immutable;

namespace HorrorHarbor.Search.Indexing;

public enum SearchIndexBuildIssueCode
{
    EmptyCatalog,
    CatalogTooLarge,
    NullDocument,
    DuplicateCandidateId,
    DuplicateCanonicalIdentity,
}

public sealed class SearchIndexBuildIssue
{
    internal SearchIndexBuildIssue(
        SearchIndexBuildIssueCode code,
        int? documentIndex,
        string? candidateId = null,
        int? conflictingDocumentIndex = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "Unknown search index build issue code.");
        }

        if (documentIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(documentIndex),
                documentIndex,
                "Document index cannot be negative.");
        }

        if (conflictingDocumentIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(conflictingDocumentIndex),
                conflictingDocumentIndex,
                "Conflicting document index cannot be negative.");
        }

        if (candidateId is not null)
        {
            HorrorHarbor.Core.Validation.CandidateIdValidator.Validate(
                candidateId,
                nameof(candidateId));
        }


        bool duplicateIssue = code is
            SearchIndexBuildIssueCode.DuplicateCandidateId or
            SearchIndexBuildIssueCode.DuplicateCanonicalIdentity;
        if (duplicateIssue !=
            (candidateId is not null && conflictingDocumentIndex.HasValue))
        {
            throw new ArgumentException(
                "Duplicate issues require candidate and conflicting source identities; other issues cannot contain them.",
                nameof(candidateId));
        }

        Code = code;
        DocumentIndex = documentIndex;
        CandidateId = candidateId;
        ConflictingDocumentIndex = conflictingDocumentIndex;
    }

    public SearchIndexBuildIssueCode Code { get; }

    public int? DocumentIndex { get; }

    public string? CandidateId { get; }

    public int? ConflictingDocumentIndex { get; }

    public string StableCode => Code switch
    {
        SearchIndexBuildIssueCode.EmptyCatalog => "empty-catalog",
        SearchIndexBuildIssueCode.CatalogTooLarge => "catalog-too-large",
        SearchIndexBuildIssueCode.NullDocument => "null-document",
        SearchIndexBuildIssueCode.DuplicateCandidateId => "duplicate-candidate-id",
        SearchIndexBuildIssueCode.DuplicateCanonicalIdentity =>
            "duplicate-canonical-identity",
        _ => throw new InvalidOperationException(
            "Unknown search index build issue code."),
    };
}

public abstract record SearchIndexBuildResult
{
    private protected SearchIndexBuildResult()
    {
    }
}

public sealed record SearchIndexBuildSucceeded : SearchIndexBuildResult
{
    internal SearchIndexBuildSucceeded(SearchIndexSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public SearchIndexSnapshot Snapshot { get; }
}

public sealed record SearchIndexBuildRejected : SearchIndexBuildResult
{
    internal SearchIndexBuildRejected(
        ImmutableArray<SearchIndexBuildIssue> issues,
        int observedDocumentCount,
        bool hasAdditionalIssues)
    {
        if (issues.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A rejected index build requires at least one issue.",
                nameof(issues));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(observedDocumentCount);

        Issues = issues;
        ObservedDocumentCount = observedDocumentCount;
        HasAdditionalIssues = hasAdditionalIssues;
    }

    public ImmutableArray<SearchIndexBuildIssue> Issues { get; }

    public int ObservedDocumentCount { get; }

    public bool HasAdditionalIssues { get; }
}
