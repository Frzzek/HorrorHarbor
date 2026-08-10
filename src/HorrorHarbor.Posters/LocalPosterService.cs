using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;

namespace HorrorHarbor.Posters;

public sealed class LocalPosterService : IPosterService, IDisposable
{
    private readonly PosterCacheOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.Ordinal);
    private bool _disposed;

    public LocalPosterService(PosterCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootDirectory) ||
            options.PixelWidth is < 240 or > 1200 ||
            options.PixelHeight is < 360 or > 1800 ||
            options.MaximumFileBytes is < 64_000 or > 10_000_000)
        {
            throw new ArgumentException("Poster cache options are outside supported safety bounds.", nameof(options));
        }

        _options = options with { RootDirectory = Path.GetFullPath(options.RootDirectory) };
    }

    public async Task<PosterAsset> GetPosterAsync(
        PosterDescriptor descriptor,
        CancellationToken cancellationToken = default) =>
        await GetPosterCoreAsync(
            descriptor,
            PosterGenerationOptions.Automatic,
            PosterSourceKind.GeneratedFallback,
            cancellationToken).ConfigureAwait(false);

    public async Task<PosterAsset> GetCustomPosterAsync(
        PosterDescriptor descriptor,
        PosterGenerationOptions options,
        CancellationToken cancellationToken = default) =>
        await GetPosterCoreAsync(
            descriptor,
            options,
            PosterSourceKind.GeneratedCustom,
            cancellationToken).ConfigureAwait(false);

    private async Task<PosterAsset> GetPosterCoreAsync(
        PosterDescriptor descriptor,
        PosterGenerationOptions options,
        PosterSourceKind sourceKind,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateDescriptor(descriptor);
        ArgumentNullException.ThrowIfNull(options);
        PosterGenerationOptions normalized = options.Normalize();
        Directory.CreateDirectory(_options.RootDirectory);

        string key = ProceduralPosterRenderer.GetCacheKey(
            descriptor,
            normalized,
            _options.PixelWidth,
            _options.PixelHeight);
        string path = Path.Combine(_options.RootDirectory, key + ".png");
        if (PngImageValidator.IsValidFile(
            path,
            _options.PixelWidth,
            _options.PixelHeight,
            _options.MaximumFileBytes))
        {
            return CreateAsset(path, wasGenerated: false, descriptor, sourceKind);
        }

        SemaphoreSlim gate = _keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (PngImageValidator.IsValidFile(
                path,
                _options.PixelWidth,
                _options.PixelHeight,
                _options.MaximumFileBytes))
            {
                return CreateAsset(path, wasGenerated: false, descriptor, sourceKind);
            }

            byte[] encoded = await Task.Run(
                () => ProceduralPosterRenderer.Render(
                    descriptor,
                    normalized,
                    _options.PixelWidth,
                    _options.PixelHeight),
                cancellationToken).ConfigureAwait(false);
            if (encoded.LongLength > _options.MaximumFileBytes || !PngImageValidator.IsValid(
                encoded,
                _options.PixelWidth,
                _options.PixelHeight,
                _options.MaximumFileBytes))
            {
                throw new InvalidDataException("Generated poster failed bounded image validation.");
            }

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, encoded, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            return CreateAsset(path, wasGenerated: true, descriptor, sourceKind);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (SemaphoreSlim gate in _keyLocks.Values)
        {
            gate.Dispose();
        }

        _keyLocks.Clear();
    }

    private PosterAsset CreateAsset(
        string path,
        bool wasGenerated,
        PosterDescriptor descriptor,
        PosterSourceKind sourceKind)
    {
        string sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var provenance = new PosterProvenance(
            sourceKind,
            "Nocturne Harbor renderer",
            null,
            descriptor.CandidateId,
            sourceKind == PosterSourceKind.GeneratedCustom
                ? "deterministic-local-custom"
                : "deterministic-local-fallback",
            1,
            descriptor.OriginalTitle,
            descriptor.OriginalLanguage,
            sourceKind == PosterSourceKind.GeneratedCustom
                ? "Original custom artwork generated locally by Nocturne Harbor."
                : "Original artwork generated locally by Nocturne Harbor.",
            null,
            File.GetLastWriteTimeUtc(path).ToString("O", CultureInfo.InvariantCulture),
            sha256);
        return new PosterAsset(path, _options.PixelWidth, _options.PixelHeight, wasGenerated, provenance);
    }

    private static void ValidateDescriptor(PosterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.CandidateId) || descriptor.CandidateId.Length > 200 ||
            string.IsNullOrWhiteSpace(descriptor.Title) || descriptor.Title.Length > 300 ||
            descriptor.PosterTone.Length > 32 || descriptor.Tags.Count > 32 ||
            descriptor.Tags.Any(static tag => tag.Length > 100))
        {
            throw new ArgumentException("Poster metadata is missing or exceeds supported bounds.", nameof(descriptor));
        }
    }
}
