using System.IO;
using System.Windows.Media;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Posters;

namespace HorrorHarbor.App.ViewModels;

public sealed class RecommendationCardViewModel : ObservableObject
{
    private RecommendationFeedback _feedback;
    private ImageSource? _posterImage;
    private string _posterStatus = "Poster artwork is loading";
    private string _posterSource = "Checking official artwork…";

    public RecommendationCardViewModel(
        RecommendationResult result,
        RecommendationFeedback feedback)
    {
        Result = result;
        _feedback = feedback;
    }

    public RecommendationResult Result { get; }
    public RecommendationCandidate Candidate => Result.Candidate;
    public double Score => Result.Score;
    public string ScoreLabel => $"{Result.Score:0.0} MATCH";
    public string Explanation => Result.Explanation;
    public string Breakdown => Result.Breakdown.Summary;
    public string AccessibleName =>
        $"{Candidate.Title}, {Candidate.YearLabel}, {Candidate.PrimaryLens}, {ScoreLabel}";

    public ImageSource? PosterImage
    {
        get => _posterImage;
        private set => SetProperty(ref _posterImage, value);
    }

    public string PosterStatus
    {
        get => _posterStatus;
        private set => SetProperty(ref _posterStatus, value);
    }

    public string PosterSource
    {
        get => _posterSource;
        private set => SetProperty(ref _posterSource, value);
    }

    public async Task LoadPosterAsync(
        IPosterImageProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        try
        {
            PosterImageResult result = await provider.GetAsync(Candidate, cancellationToken).ConfigureAwait(true);
            ApplyPoster(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PosterStatus = "Poster loading was canceled";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            PosterImage = null;
            PosterSource = "ARTWORK UNAVAILABLE";
            PosterStatus = $"Artwork unavailable; showing accessible title treatment for {Candidate.Title}";
        }
    }

    public void ApplyPoster(PosterImageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PosterImage = result.Image;
        (PosterSource, PosterStatus) = result.Provenance?.SourceKind switch
        {
            PosterSourceKind.TmdbOfficial => (
                "OFFICIAL ARTWORK · TMDB",
                $"Official TMDB poster for {Candidate.Title}; confidence {result.Provenance.MatchConfidence:P0}"),
            PosterSourceKind.GeneratedCustom => (
                "CUSTOM GENERATED",
                $"User-selected custom artwork generated locally for {Candidate.Title}"),
            _ => (
                "GENERATED FALLBACK",
                $"Official artwork unavailable or not configured; generated fallback for {Candidate.Title}"),
        };
    }

    public RecommendationFeedback Feedback
    {
        get => _feedback;
        set
        {
            if (SetProperty(ref _feedback, value))
            {
                OnPropertyChanged(nameof(FeedbackLabel));
            }
        }
    }

    public string FeedbackLabel => Feedback == RecommendationFeedback.None
        ? "No feedback yet"
        : Feedback.ToString();
}
