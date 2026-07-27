using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Indexing;

public interface ISearchIndexBuilder
{
    SearchIndexBuildResult Build(
        IEnumerable<HorrorSearchDocument> documents,
        CancellationToken cancellationToken = default);
}

public sealed class SearchIndexBuilder : ISearchIndexBuilder
{
    public const int MaximumDocuments = 50_000;
    public const int MaximumReportedIssues = 32;

    public SearchIndexBuildResult Build(
        IEnumerable<HorrorSearchDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var materialized = new List<SourceDocument>();
        var issues = ImmutableArray.CreateBuilder<SearchIndexBuildIssue>();
        bool hasAdditionalIssues = false;
        int observedDocumentCount = 0;

        foreach (HorrorSearchDocument? document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sourceIndex = observedDocumentCount;
            observedDocumentCount++;

            if (observedDocumentCount > MaximumDocuments)
            {
                AddIssue(
                    issues,
                    SearchIndexBuildIssueCode.CatalogTooLarge,
                    sourceIndex,
                    ref hasAdditionalIssues);
                break;
            }

            if (document is null)
            {
                AddIssue(
                    issues,
                    SearchIndexBuildIssueCode.NullDocument,
                    sourceIndex,
                    ref hasAdditionalIssues);
                continue;
            }

            materialized.Add(new SourceDocument(document, sourceIndex));
        }

        if (materialized.Count == 0 && issues.Count == 0)
        {
            AddIssue(
                issues,
                SearchIndexBuildIssueCode.EmptyCatalog,
                documentIndex: null,
                ref hasAdditionalIssues);
        }

        if (observedDocumentCount > MaximumDocuments || materialized.Count == 0)
        {
            return Reject(
                issues,
                observedDocumentCount,
                hasAdditionalIssues);
        }

        ValidateIdentities(
            materialized,
            issues,
            ref hasAdditionalIssues,
            cancellationToken);
        if (issues.Count != 0)
        {
            return Reject(
                issues,
                observedDocumentCount,
                hasAdditionalIssues);
        }

        materialized.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(
                left.Document.CandidateId,
                right.Document.CandidateId));

        string catalogFingerprint;
        using (var fingerprintBuilder = new CatalogFingerprintBuilder())
        {
            catalogFingerprint = fingerprintBuilder.Build(
                [.. materialized.Select(item => item.Document)],
                cancellationToken);
        }

        var assembler = new SearchIndexAssembler(materialized.Count);
        for (int documentId = 0; documentId < materialized.Count; documentId++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assembler.Add(materialized[documentId].Document, documentId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new SearchIndexBuildSucceeded(
            assembler.Build(catalogFingerprint, cancellationToken));
    }

    private static void ValidateIdentities(
        List<SourceDocument> documents,
        ImmutableArray<SearchIndexBuildIssue>.Builder issues,
        ref bool hasAdditionalIssues,
        CancellationToken cancellationToken)
    {
        var candidateIds = new Dictionary<string, SourceDocument>(
            StringComparer.Ordinal);
        var identities = new Dictionary<CanonicalIdentity, SourceDocument>();

        for (int index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceDocument current = documents[index];
            HorrorSearchDocument document = current.Document;

            if (!candidateIds.TryAdd(document.CandidateId, current))
            {
                SourceDocument conflict = candidateIds[document.CandidateId];
                AddIssue(
                    issues,
                    SearchIndexBuildIssueCode.DuplicateCandidateId,
                    current.SourceIndex,
                    document.CandidateId,
                    conflict.SourceIndex,
                    ref hasAdditionalIssues);
            }

            string normalizedTitle = SearchTextNormalizerV1
                .Normalize(document.Candidate.Title)
                .Value;
            var identity = new CanonicalIdentity(
                normalizedTitle,
                document.Candidate.ReleaseYear);
            if (!identities.TryAdd(identity, current))
            {
                SourceDocument conflict = identities[identity];
                AddIssue(
                    issues,
                    SearchIndexBuildIssueCode.DuplicateCanonicalIdentity,
                    current.SourceIndex,
                    document.CandidateId,
                    conflict.SourceIndex,
                    ref hasAdditionalIssues);
            }
        }
    }

    private static void AddIssue(
        ImmutableArray<SearchIndexBuildIssue>.Builder issues,
        SearchIndexBuildIssueCode code,
        int? documentIndex,
        ref bool hasAdditionalIssues)
        => AddIssue(
            issues,
            code,
            documentIndex,
            candidateId: null,
            conflictingDocumentIndex: null,
            ref hasAdditionalIssues);

    private static void AddIssue(
        ImmutableArray<SearchIndexBuildIssue>.Builder issues,
        SearchIndexBuildIssueCode code,
        int? documentIndex,
        string? candidateId,
        int? conflictingDocumentIndex,
        ref bool hasAdditionalIssues)
    {
        if (issues.Count < MaximumReportedIssues)
        {
            issues.Add(new SearchIndexBuildIssue(
                code,
                documentIndex,
                candidateId,
                conflictingDocumentIndex));
        }
        else
        {
            hasAdditionalIssues = true;
        }
    }

    private static SearchIndexBuildRejected Reject(
        ImmutableArray<SearchIndexBuildIssue>.Builder issues,
        int observedDocumentCount,
        bool hasAdditionalIssues) => new(
        issues.ToImmutable(),
        observedDocumentCount,
        hasAdditionalIssues);

    private readonly record struct CanonicalIdentity(
        string NormalizedTitle,
        int? ReleaseYear);

    private readonly record struct SourceDocument(
        HorrorSearchDocument Document,
        int SourceIndex);
}
