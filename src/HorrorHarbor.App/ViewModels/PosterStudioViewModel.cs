using System.IO;
using System.Windows.Media;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Posters;

namespace HorrorHarbor.App.ViewModels;

public sealed record PosterStudioChoice<T>(string Label, T? Value)
    where T : struct, Enum;

public sealed class PosterStudioViewModel : ObservableObject
{
    private readonly RecommendationCardViewModel _card;
    private readonly IPosterImageProvider _imageProvider;
    private readonly PosterCustomizationStore _customizationStore;
    private PosterStudioChoice<PosterCompositionFamily> _selectedComposition;
    private PosterStudioChoice<PosterEraStyle> _selectedEra;
    private PosterStudioChoice<PosterTitleStyle> _selectedTitleStyle;
    private PosterStudioChoice<PosterTextureStyle> _selectedTexture;
    private int _variationOffset;
    private string _accentTone = string.Empty;
    private ImageSource? _previewImage;
    private bool _hasPreview;
    private bool _isBusy;
    private string _status = "Choose a style, preview it, then apply only if it fits movie night.";

    public PosterStudioViewModel(
        RecommendationCardViewModel card,
        IPosterImageProvider imageProvider,
        PosterCustomizationStore customizationStore)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(imageProvider);
        ArgumentNullException.ThrowIfNull(customizationStore);

        _card = card;
        _imageProvider = imageProvider;
        _customizationStore = customizationStore;
        CompositionOptions = CreateChoices<PosterCompositionFamily>("Automatic from film metadata");
        EraOptions = CreateChoices<PosterEraStyle>("Automatic from release year");
        TitleStyleOptions = CreateChoices<PosterTitleStyle>("Automatic typography");
        TextureOptions = CreateChoices<PosterTextureStyle>("Automatic texture", PosterTextureStyle.Automatic);
        _selectedComposition = CompositionOptions[0];
        _selectedEra = EraOptions[0];
        _selectedTitleStyle = TitleStyleOptions[0];
        _selectedTexture = TextureOptions[0];

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy, HandleError);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy && HasPreview, HandleError);
        ResetCommand = new AsyncRelayCommand(ResetAsync, () => !IsBusy, HandleError);
    }

    public IReadOnlyList<PosterStudioChoice<PosterCompositionFamily>> CompositionOptions { get; }
    public IReadOnlyList<PosterStudioChoice<PosterEraStyle>> EraOptions { get; }
    public IReadOnlyList<PosterStudioChoice<PosterTitleStyle>> TitleStyleOptions { get; }
    public IReadOnlyList<PosterStudioChoice<PosterTextureStyle>> TextureOptions { get; }

    public PosterStudioChoice<PosterCompositionFamily> SelectedComposition
    {
        get => _selectedComposition;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedComposition, value))
            {
                InvalidatePreview();
            }
        }
    }

    public PosterStudioChoice<PosterEraStyle> SelectedEra
    {
        get => _selectedEra;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedEra, value))
            {
                InvalidatePreview();
            }
        }
    }

    public PosterStudioChoice<PosterTitleStyle> SelectedTitleStyle
    {
        get => _selectedTitleStyle;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedTitleStyle, value))
            {
                InvalidatePreview();
            }
        }
    }

    public PosterStudioChoice<PosterTextureStyle> SelectedTexture
    {
        get => _selectedTexture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedTexture, value))
            {
                InvalidatePreview();
            }
        }
    }

    public int VariationOffset
    {
        get => _variationOffset;
        set
        {
            if (SetProperty(ref _variationOffset, Math.Clamp(value, 0, 11)))
            {
                InvalidatePreview();
            }
        }
    }

    public string AccentTone
    {
        get => _accentTone;
        set
        {
            if (SetProperty(ref _accentTone, value ?? string.Empty))
            {
                InvalidatePreview();
            }
        }
    }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set
        {
            if (SetProperty(ref _hasPreview, value))
            {
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                PreviewCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
                ResetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PosterGenerationOptions? saved = await _customizationStore.LoadAsync(
                _card.Candidate.CandidateId,
                cancellationToken).ConfigureAwait(true);
            if (saved is null)
            {
                return;
            }

            ApplyOptions(saved);
            Status = "Saved custom poster is active. Preview changes before replacing it.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Poster-studio setup was canceled.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            HandleError(exception);
        }
    }

    internal PosterGenerationOptions CaptureOptions() => new PosterGenerationOptions(
        SelectedComposition.Value,
        SelectedEra.Value,
        SelectedTitleStyle.Value,
        SelectedTexture.Value ?? PosterTextureStyle.Automatic,
        VariationOffset,
        string.IsNullOrWhiteSpace(AccentTone) ? null : AccentTone).Normalize();

    private async Task PreviewAsync()
    {
        try
        {
            IsBusy = true;
            Status = "Rendering a private local preview…";
            PosterImageResult result = await _imageProvider.GetCustomAsync(
                _card.Candidate,
                CaptureOptions()).ConfigureAwait(true);
            PreviewImage = result.Image;
            HasPreview = true;
            Status = "Preview ready. Nothing has been saved or uploaded.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAsync()
    {
        try
        {
            IsBusy = true;
            PosterGenerationOptions options = CaptureOptions();
            if (options.IsAutomatic)
            {
                await ResetCoreAsync().ConfigureAwait(true);
                return;
            }

            Status = "Validating and saving the custom poster choice…";
            PosterImageResult result = await _imageProvider.GetCustomAsync(
                _card.Candidate,
                options).ConfigureAwait(true);
            await _customizationStore.SaveAsync(
                _card.Candidate.CandidateId,
                options).ConfigureAwait(true);
            _card.ApplyPoster(result);
            PreviewImage = null;
            HasPreview = false;
            Status = "Custom generated poster applied. The choice is stored in encrypted local settings.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            IsBusy = true;
            await ResetCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetCoreAsync()
    {
        Status = "Restoring the normal official-first artwork hierarchy…";
        PosterImageResult result = await _imageProvider.GetAutomaticAsync(
            _card.Candidate).ConfigureAwait(true);
        await _customizationStore.ClearAsync(_card.Candidate.CandidateId).ConfigureAwait(true);
        _card.ApplyPoster(result);
        ApplyOptions(PosterGenerationOptions.Automatic);
        PreviewImage = null;
        HasPreview = false;
        Status = "Custom choice removed. Official artwork is preferred when configured; generated fallback remains available.";
    }

    private void ApplyOptions(PosterGenerationOptions options)
    {
        PosterGenerationOptions normalized = options.Normalize();
        _selectedComposition = Find(CompositionOptions, normalized.CompositionFamily);
        _selectedEra = Find(EraOptions, normalized.EraStyle);
        _selectedTitleStyle = Find(TitleStyleOptions, normalized.TitleStyle);
        PosterTextureStyle? texture = normalized.TextureStyle == PosterTextureStyle.Automatic
            ? null
            : normalized.TextureStyle;
        _selectedTexture = Find(TextureOptions, texture);
        _variationOffset = normalized.VariationOffset;
        _accentTone = normalized.AccentTone ?? string.Empty;
        OnPropertyChanged(nameof(SelectedComposition));
        OnPropertyChanged(nameof(SelectedEra));
        OnPropertyChanged(nameof(SelectedTitleStyle));
        OnPropertyChanged(nameof(SelectedTexture));
        OnPropertyChanged(nameof(VariationOffset));
        OnPropertyChanged(nameof(AccentTone));
    }

    private void InvalidatePreview()
    {
        if (!HasPreview)
        {
            return;
        }

        PreviewImage = null;
        HasPreview = false;
        Status = "Style changed. Render a new preview before applying.";
    }

    private void HandleError(Exception exception)
    {
        _ = exception;
        Status = "Poster generation failed safely. Existing artwork and saved choices were not changed.";
    }

    private static PosterStudioChoice<T>[] CreateChoices<T>(
        string automaticLabel,
        T? excluded = null)
        where T : struct, Enum
    {
        var choices = new List<PosterStudioChoice<T>>
        {
            new(automaticLabel, null),
        };
        choices.AddRange(Enum.GetValues<T>()
            .Where(value => excluded is null || !EqualityComparer<T>.Default.Equals(value, excluded.Value))
            .Select(value => new PosterStudioChoice<T>(SplitWords(value.ToString()), value)));
        return choices.ToArray();
    }

    private static PosterStudioChoice<T> Find<T>(
        IReadOnlyList<PosterStudioChoice<T>> choices,
        T? value)
        where T : struct, Enum =>
        choices.First(choice => Nullable.Equals(choice.Value, value));

    private static string SplitWords(string value) => string.Concat(
        value.Select((character, index) => index > 0 && char.IsUpper(character)
            ? $" {character}"
            : character.ToString()));
}
