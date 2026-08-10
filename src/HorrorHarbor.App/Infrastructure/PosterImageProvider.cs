using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Posters;

namespace HorrorHarbor.App.Infrastructure;

public interface IPosterImageProvider
{
    Task<PosterImageResult> GetAsync(
        RecommendationCandidate candidate,
        CancellationToken cancellationToken = default);

    Task<PosterImageResult> GetCustomAsync(
        RecommendationCandidate candidate,
        PosterGenerationOptions options,
        CancellationToken cancellationToken = default);

    Task<PosterImageResult> GetAutomaticAsync(
        RecommendationCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed record PosterImageResult(ImageSource Image, PosterProvenance? Provenance);

public sealed class PosterImageProvider : IPosterImageProvider
{
    private readonly IPosterService _posterService;
    private readonly LocalPosterService _localPosterService;
    private readonly PosterCustomizationStore _customizationStore;

    public PosterImageProvider(
        IPosterService posterService,
        LocalPosterService localPosterService,
        PosterCustomizationStore customizationStore)
    {
        _posterService = posterService;
        _localPosterService = localPosterService;
        _customizationStore = customizationStore;
    }

    public async Task<PosterImageResult> GetAsync(
        RecommendationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        PosterDescriptor descriptor = CreateDescriptor(candidate);
        PosterGenerationOptions? customization = await _customizationStore.LoadAsync(
            candidate.CandidateId,
            cancellationToken).ConfigureAwait(false);
        PosterAsset asset = customization is null
            ? await _posterService.GetPosterAsync(descriptor, cancellationToken).ConfigureAwait(false)
            : await _localPosterService.GetCustomPosterAsync(
                descriptor,
                customization,
                cancellationToken).ConfigureAwait(false);

        return await CreateResultAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PosterImageResult> GetCustomAsync(
        RecommendationCandidate candidate,
        PosterGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);
        PosterAsset asset = await _localPosterService.GetCustomPosterAsync(
            CreateDescriptor(candidate),
            options,
            cancellationToken).ConfigureAwait(false);
        return await CreateResultAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PosterImageResult> GetAutomaticAsync(
        RecommendationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        PosterAsset asset = await _posterService.GetPosterAsync(
            CreateDescriptor(candidate),
            cancellationToken).ConfigureAwait(false);
        return await CreateResultAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PosterImageResult> CreateResultAsync(
        PosterAsset asset,
        CancellationToken cancellationToken)
    {
        BitmapImage image = await Task.Run(() => Decode(asset), cancellationToken).ConfigureAwait(false);
        return new PosterImageResult(image, asset.Provenance);
    }

    private static PosterDescriptor CreateDescriptor(RecommendationCandidate candidate) => new(
        candidate.CandidateId,
        candidate.Title,
        candidate.ReleaseYear,
        candidate.PrimaryLens,
        candidate.PosterTone,
        candidate.Tags,
        candidate.OriginalTitle,
        candidate.OriginalLanguage,
        candidate.TmdbId,
        candidate.SecondaryLens,
        candidate.Franchise,
        candidate.ClampedScareIntensity,
        candidate.ClampedGoreIntensity,
        candidate.ClampedDreadIntensity);

    private static BitmapImage Decode(PosterAsset asset)
    {
        using var stream = new FileStream(
            asset.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = asset.PixelWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        if (bitmap.PixelWidth != asset.PixelWidth || bitmap.PixelHeight != asset.PixelHeight)
        {
            throw new InvalidDataException("Decoded poster dimensions did not match the validated asset.");
        }

        bitmap.Freeze();
        return bitmap;
    }
}
