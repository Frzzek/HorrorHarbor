using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Indexing;

internal sealed class CatalogFingerprintBuilder : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IncrementalHash _hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _textBuffer = new byte[
        Utf8.GetMaxByteCount(
            SearchDocumentLimits.MaximumSearchableTextCodeUnits)];

    public string Build(
        IReadOnlyList<HorrorSearchDocument> documents,
        CancellationToken cancellationToken)
    {
        AppendInt32(SearchIndexSnapshot.SchemaVersion);
        AppendInt32(SearchTextNormalizerV1.Version);
        AppendInt32(documents.Count);

        foreach (HorrorSearchDocument document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendDocument(document);
        }

        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    public void Dispose() => _hash.Dispose();

    private void AppendDocument(HorrorSearchDocument document)
    {
        RecommendationCandidate candidate = document.Candidate;

        AppendString(candidate.CandidateId);
        AppendString(candidate.Title);
        AppendNullableInt32(candidate.ReleaseYear);
        AppendString(candidate.ScopeTier);
        AppendNullableString(candidate.PrimaryLens);
        AppendNullableString(candidate.SecondaryLens);
        AppendNullableString(candidate.Franchise);
        AppendInt32(candidate.ScareIntensity);
        AppendInt32(candidate.GoreIntensity);
        AppendInt32(candidate.DreadIntensity);
        AppendBoolean(candidate.IsSlasher);
        AppendBoolean(candidate.IsExtreme);
        AppendNullableInt32(candidate.RuntimeMinutes);
        AppendDouble(candidate.DiscoveryScore);
        AppendDouble(candidate.QualityScore);
        AppendDouble(candidate.AvailabilityScore);
        AppendString(candidate.AvailabilityLabel);
        AppendString(candidate.PosterTone);
        AppendDouble(candidate.UserFeedbackBoost);
        AppendNullableString(candidate.OriginalTitle);
        AppendNullableString(candidate.OriginalLanguage);
        AppendNullableInt32(candidate.TmdbId);

        AppendInt32(candidate.Tags.Count);
        foreach (string tag in candidate.Tags)
        {
            AppendString(tag);
        }

        AppendString(document.SourceRevision);
        AppendInt32((int)document.AvailabilityState);

        AppendInt32(document.Aliases.Length);
        foreach (SearchAlias alias in document.Aliases)
        {
            AppendString(alias.Value);
            AppendProvenance(alias.Provenance);
        }

        AppendInt32(document.AvailabilityFacts.Length);
        foreach (SearchAvailabilityFact fact in document.AvailabilityFacts)
        {
            AppendString(fact.ProviderValue);
            AppendProvenance(fact.Provenance);
        }
    }

    private void AppendProvenance(SearchMetadataProvenance provenance)
    {
        AppendInt32((int)provenance.SourceKind);
        AppendString(provenance.SourceIdentifier);
        AppendString(provenance.SourceRevision);
    }

    private void AppendNullableString(string? value)
    {
        AppendBoolean(value is not null);
        if (value is not null)
        {
            AppendString(value);
        }
    }

    private void AppendString(string value)
    {
        int byteCount = Utf8.GetBytes(value, _textBuffer);
        AppendInt32(byteCount);
        _hash.AppendData(_textBuffer.AsSpan(0, byteCount));
    }

    private void AppendNullableInt32(int? value)
    {
        AppendBoolean(value.HasValue);
        if (value.HasValue)
        {
            AppendInt32(value.Value);
        }
    }

    private void AppendInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _hash.AppendData(buffer);
    }

    private void AppendDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(
            buffer,
            BitConverter.DoubleToInt64Bits(value));
        _hash.AppendData(buffer);
    }

    private void AppendBoolean(bool value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value ? (byte)1 : (byte)0;
        _hash.AppendData(buffer);
    }
}
