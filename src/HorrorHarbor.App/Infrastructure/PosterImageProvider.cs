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
}

public sealed record PosterImageResult(ImageSource Image, PosterProvenance? Provenance);

public sealed class PosterImageProvider : IPosterImageProvider
{
    private readonly IPosterService _posterService;

    public PosterImageProvider(IPosterService posterService) => _posterService = posterService;

    public async Task<PosterImageResult> GetAsync(
        RecommendationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        PosterAsset asset = await _posterService.GetPosterAsync(
            new PosterDescriptor(
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
                candidate.ClampedDreadIntensity),
            cancellationToken).ConfigureAwait(false);

        BitmapImage image = await Task.Run(() => Decode(asset), cancellationToken).ConfigureAwait(false);
        return new PosterImageResult(image, asset.Provenance);
    }

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
