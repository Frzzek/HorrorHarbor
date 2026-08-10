using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Recommendations.Engine;
using HorrorHarbor.Posters;

namespace HorrorHarbor.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ICandidateCatalog _candidateCatalog;
    private readonly IFeedbackStore _feedbackStore;
    private readonly IFeedbackCoordinator _feedbackCoordinator;
    private readonly IUserDataStore _userDataStore;
    private readonly IPlexSyncService _plexSyncService;
    private readonly RecommendationQueryService _recommendationQueryService;
    private readonly ICatalogSearchService _catalogSearchService;
    private readonly DiscoveryMoodService _discoveryMoodService;
    private readonly IPlexHistoryProjectionService _plexHistoryProjectionService;
    private readonly ISecureErrorReporter _errorReporter;
    private readonly IPosterImageProvider _posterImageProvider;
    private readonly IPosterCredentialStore _posterCredentialStore;
    private readonly PosterCustomizationStore _posterCustomizationStore;
    private CancellationTokenSource? _posterLoadCancellation;
    private CancellationTokenSource? _plexSyncCancellation;

    private IReadOnlyList<TitleCatalogItem> _tasteSource = Array.Empty<TitleCatalogItem>();
    private IReadOnlyList<RecommendationCandidate> _allCandidates = Array.Empty<RecommendationCandidate>();
    private Dictionary<string, RecommendationFeedback> _feedback = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _watchedExclusionKeys = new(StringComparer.OrdinalIgnoreCase);
    private DashboardStats _stats = DashboardStats.Empty;
    private string _statusText = "Ready to ignite recommendation intelligence.";
    private bool _isBusy;
    private double _desiredScare = 4;
    private double _desiredGore = 4;
    private double _desiredDread = 3;
    private double _novelty = 0.78;
    private bool _preferSlashers = true;
    private bool _preferExtreme;
    private int _recommendationCount = 12;
    private string _plexServerUrl = "https://127.0.0.1:32400";
    private bool _allowInsecurePlexHttp;
    private string _plexStatus = "Plex is not connected.";
    private string _artworkStatus = "Official posters are not configured; generated fallbacks are active.";
    private string _searchText = string.Empty;
    private string? _searchDiagnostic;
    private string _selectedSort = "Best match";
    private FilmDetailsViewModel? _selectedFilmDetails;
    private bool _isInitialized;
    private FeedbackChange? _lastFeedbackChange;

    private const string PlexConnectionId = "primary";
    private const string MoodSettingKey = "discovery.mood.v1";

    public MainWindowViewModel(
        ICandidateCatalog candidateCatalog,
        IFeedbackStore feedbackStore,
        IFeedbackCoordinator feedbackCoordinator,
        IUserDataStore userDataStore,
        IPlexSyncService plexSyncService,
        RecommendationQueryService recommendationQueryService,
        ICatalogSearchService catalogSearchService,
        DiscoveryMoodService discoveryMoodService,
        IPlexHistoryProjectionService plexHistoryProjectionService,
        ISecureErrorReporter errorReporter,
        IPosterImageProvider posterImageProvider,
        IPosterCredentialStore posterCredentialStore,
        PosterCustomizationStore posterCustomizationStore)
    {
        _candidateCatalog = candidateCatalog;
        _feedbackStore = feedbackStore;
        _feedbackCoordinator = feedbackCoordinator;
        _userDataStore = userDataStore;
        _plexSyncService = plexSyncService;
        _recommendationQueryService = recommendationQueryService;
        _catalogSearchService = catalogSearchService;
        _discoveryMoodService = discoveryMoodService;
        _plexHistoryProjectionService = plexHistoryProjectionService;
        _errorReporter = errorReporter;
        _posterImageProvider = posterImageProvider;
        _posterCredentialStore = posterCredentialStore;
        _posterCustomizationStore = posterCustomizationStore;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy, HandleCommandError);
        RefreshRecommendationsCommand = new AsyncRelayCommand(
            RefreshRecommendationsAsync,
            () => !IsBusy && _allCandidates.Count > 0,
            HandleCommandError);
        SurpriseMeCommand = new AsyncRelayCommand(SurpriseMeAsync, () => !IsBusy, HandleCommandError);
        ResetMoodCommand = new AsyncRelayCommand(ResetMoodAsync, () => !IsBusy, HandleCommandError);
        ClearSearchCommand = new AsyncRelayCommand(ClearSearchAsync, () => !IsBusy, HandleCommandError);
        SyncPlexCommand = new AsyncRelayCommand(SyncPlexAsync, () => !IsBusy, HandleCommandError);
        CancelPlexSyncCommand = new AsyncRelayCommand(
            CancelPlexSyncAsync,
            () => _plexSyncCancellation is not null,
            HandleCommandError);
        UndoFeedbackCommand = new AsyncRelayCommand(
            UndoFeedbackAsync,
            () => !IsBusy && _lastFeedbackChange is not null,
            HandleCommandError);
        MarkInterestedCommand = new RelayCommand<RecommendationCardViewModel>(
            card => _ = ApplyFeedbackAsync(card, RecommendationFeedback.Interested));
        MarkNotNowCommand = new RelayCommand<RecommendationCardViewModel>(
            card => _ = ApplyFeedbackAsync(card, RecommendationFeedback.NotNow));
        MarkDislikeCommand = new RelayCommand<RecommendationCardViewModel>(
            card => _ = ApplyFeedbackAsync(card, RecommendationFeedback.Dislike));
        MarkWatchedCommand = new RelayCommand<RecommendationCardViewModel>(
            card => _ = ApplyFeedbackAsync(card, RecommendationFeedback.Watched));
    }

    public DashboardStats Stats
    {
        get => _stats;
        private set => SetProperty(ref _stats, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoadCommand.RaiseCanExecuteChanged();
                RefreshRecommendationsCommand.RaiseCanExecuteChanged();
                SurpriseMeCommand.RaiseCanExecuteChanged();
                SyncPlexCommand.RaiseCanExecuteChanged();
                CancelPlexSyncCommand.RaiseCanExecuteChanged();
                UndoFeedbackCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double DesiredScare
    {
        get => _desiredScare;
        set
        {
            if (SetProperty(ref _desiredScare, value))
            {
                OnPropertyChanged(nameof(MoodSummary));
            }
        }
    }

    public double DesiredGore
    {
        get => _desiredGore;
        set
        {
            if (SetProperty(ref _desiredGore, value))
            {
                OnPropertyChanged(nameof(MoodSummary));
            }
        }
    }

    public double DesiredDread
    {
        get => _desiredDread;
        set
        {
            if (SetProperty(ref _desiredDread, value))
            {
                OnPropertyChanged(nameof(MoodSummary));
            }
        }
    }

    public double Novelty
    {
        get => _novelty;
        set
        {
            if (SetProperty(ref _novelty, value))
            {
                OnPropertyChanged(nameof(MoodSummary));
            }
        }
    }

    public bool PreferSlashers
    {
        get => _preferSlashers;
        set => SetProperty(ref _preferSlashers, value);
    }

    public bool PreferExtreme
    {
        get => _preferExtreme;
        set => SetProperty(ref _preferExtreme, value);
    }

    public int RecommendationCount
    {
        get => _recommendationCount;
        set => SetProperty(ref _recommendationCount, Math.Clamp(value, 6, 30));
    }

    public string PlexServerUrl
    {
        get => _plexServerUrl;
        set => SetProperty(ref _plexServerUrl, value);
    }

    public bool AllowInsecurePlexHttp
    {
        get => _allowInsecurePlexHttp;
        set => SetProperty(ref _allowInsecurePlexHttp, value);
    }

    public string PlexStatus
    {
        get => _plexStatus;
        private set => SetProperty(ref _plexStatus, value);
    }

    public string ArtworkStatus
    {
        get => _artworkStatus;
        private set => SetProperty(ref _artworkStatus, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value) && _isInitialized && !IsBusy)
            {
                _ = RefreshRecommendationsCoreAsync();
            }
        }
    }

    public string? SearchDiagnostic
    {
        get => _searchDiagnostic;
        private set
        {
            if (SetProperty(ref _searchDiagnostic, value))
            {
                OnPropertyChanged(nameof(HasSearchDiagnostic));
            }
        }
    }

    public bool HasSearchDiagnostic => SearchDiagnostic is not null;

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value) && _isInitialized && !IsBusy)
            {
                _ = RefreshRecommendationsCoreAsync();
            }
        }
    }

    public FilmDetailsViewModel? SelectedFilmDetails
    {
        get => _selectedFilmDetails;
        private set
        {
            if (SetProperty(ref _selectedFilmDetails, value))
            {
                OnPropertyChanged(nameof(IsFilmDetailsOpen));
            }
        }
    }

    public bool IsFilmDetailsOpen => SelectedFilmDetails is not null;

    public string MoodSummary =>
        $"Scare {DesiredScare:0} · Gore {DesiredGore:0} · Dread {DesiredDread:0} · Discovery {Novelty:P0}";

    public string ResultsSummary => Recommendations.Count == 1
        ? "1 film ready for tonight"
        : $"{Recommendations.Count:N0} films ready for tonight";

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "Best match",
        "Newest first",
        "Scariest first",
        "Shortest first",
    };

    public ObservableCollection<RecommendationCardViewModel> Recommendations { get; } = new();
    public ObservableCollection<TitleCatalogItem> TasteSignals { get; } = new();

    public void OpenFilmDetails(RecommendationCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        SelectedFilmDetails = CreateFilmDetails(card);
    }

    public void CloseFilmDetails() => SelectedFilmDetails = null;

    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand RefreshRecommendationsCommand { get; }
    public AsyncRelayCommand SurpriseMeCommand { get; }
    public AsyncRelayCommand ResetMoodCommand { get; }
    public AsyncRelayCommand ClearSearchCommand { get; }
    public AsyncRelayCommand SyncPlexCommand { get; }
    public AsyncRelayCommand CancelPlexSyncCommand { get; }
    public AsyncRelayCommand UndoFeedbackCommand { get; }
    public RelayCommand<RecommendationCardViewModel> MarkInterestedCommand { get; }
    public RelayCommand<RecommendationCardViewModel> MarkNotNowCommand { get; }
    public RelayCommand<RecommendationCardViewModel> MarkDislikeCommand { get; }
    public RelayCommand<RecommendationCardViewModel> MarkWatchedCommand { get; }

    public Task InitializeAsync() => LoadAsync();

    public async Task SaveTmdbTokenAsync(string token)
    {
        try
        {
            await _posterCredentialStore.SaveTmdbTokenAsync(token).ConfigureAwait(true);
            ArtworkStatus = "Official TMDB posters enabled. Refreshing recommendations…";
            await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
            ArtworkStatus = "Official TMDB posters enabled; ambiguous or unavailable matches use generated fallbacks.";
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception)
        {
            ArtworkStatus = "TMDB setup failed safely; generated fallbacks remain active.";
            await SetSafeFailureAsync(exception, "tmdb-credential-save").ConfigureAwait(true);
        }
    }

    public async Task DeleteTmdbTokenAsync()
    {
        try
        {
            await _posterCredentialStore.DeleteTmdbTokenAsync().ConfigureAwait(true);
            ArtworkStatus = "Official posters disabled; generated fallbacks are active.";
            await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ArtworkStatus = "TMDB credential removal failed safely; existing artwork settings were not changed.";
            await SetSafeFailureAsync(exception, "tmdb-credential-delete").ConfigureAwait(true);
        }
    }

    public async Task ConnectPlexAsync(string token)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            PlexStatus = "Testing the secure Plex connection...";
            var settings = new PlexConnectionSettings(PlexServerUrl, AllowInsecurePlexHttp);
            PlexServerIdentity identity = await _plexSyncService.ConnectAsync(
                PlexConnectionId,
                settings,
                token).ConfigureAwait(true);
            PlexStatus = $"Connected to {identity.Name} ({identity.Version}). Syncing history...";
            await SyncPlexCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            PlexStatus = "Plex connection failed safely. The token was not displayed or logged.";
            await SetSafeFailureAsync(exception, "plex-connect").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _userDataStore.InitializeAsync().ConfigureAwait(true);
            await LoadMoodSettingsAsync().ConfigureAwait(true);
            StatusText = "Loading the local horror catalog...";
            _tasteSource = Array.Empty<TitleCatalogItem>();
            _watchedExclusionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allCandidates = await _candidateCatalog.LoadAsync().ConfigureAwait(true);
            _catalogSearchService.PublishCatalog(_allCandidates);
            await MergePlexHistoryAsync().ConfigureAwait(true);
            UpdateLocalProfileSummary();
            _feedback = new Dictionary<string, RecommendationFeedback>(
                await _feedbackStore.LoadAsync().ConfigureAwait(true),
                StringComparer.OrdinalIgnoreCase);
            await LoadArtworkStatusAsync().ConfigureAwait(true);

            var savedPlexServer = await _userDataStore.LoadPlexServerAsync(PlexConnectionId).ConfigureAwait(true);
            if (savedPlexServer is { } server)
            {
                PlexServerUrl = server.Settings.ServerUrl;
                AllowInsecurePlexHttp = server.Settings.AllowInsecureHttp;
                PlexStatus = server.LastSyncedUtc is { } lastSynced
                    ? $"Connected to {server.Identity.Name}. Last synced {lastSynced.ToLocalTime():g}."
                    : $"Connected to {server.Identity.Name}. History has not been synced yet.";
            }

            RefreshTasteSignals();

            _isInitialized = true;
            await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await SetSafeFailureAsync(exception, "recommendation-launch").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadArtworkStatusAsync()
    {
        try
        {
            string? token = await _posterCredentialStore.LoadTmdbTokenAsync().ConfigureAwait(true);
            ArtworkStatus = string.IsNullOrWhiteSpace(token)
                ? "Official posters are not configured; generated fallbacks are active."
                : "Official TMDB posters enabled; ambiguous or unavailable matches use generated fallbacks.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ArtworkStatus = "TMDB credential status could not be read; generated fallbacks remain available.";
            await SetSafeFailureAsync(exception, "tmdb-credential-load").ConfigureAwait(true);
        }
    }

    private async Task RefreshRecommendationsAsync()
    {
        try
        {
            IsBusy = true;
            await SaveMoodSettingsAsync().ConfigureAwait(true);
            await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await SetSafeFailureAsync(exception, "recommendation-refresh").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task SurpriseMeAsync()
    {
        ApplyMood(_discoveryMoodService.CreateSurprise());
        return RefreshRecommendationsAsync();
    }

    private Task ResetMoodAsync()
    {
        ApplyMood(DiscoveryMoodService.CreateReset());
        return RefreshRecommendationsAsync();
    }

    private Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        SearchDiagnostic = null;
        return Task.CompletedTask;
    }

    private async Task LoadMoodSettingsAsync()
    {
        string? value = await _userDataStore.LoadSettingAsync(MoodSettingKey).ConfigureAwait(true);
        if (!DiscoveryMoodService.TryDeserialize(value, out DiscoveryMood mood))
        {
            return;
        }

        ApplyMood(mood);
    }

    private Task SaveMoodSettingsAsync()
    {
        string value = DiscoveryMoodService.Serialize(CaptureMood());
        return _userDataStore.SaveSettingAsync(MoodSettingKey, value);
    }

    private DiscoveryMood CaptureMood() => new(
        DesiredScare,
        DesiredGore,
        DesiredDread,
        Novelty,
        PreferSlashers,
        PreferExtreme,
        RecommendationCount);

    private void ApplyMood(DiscoveryMood mood)
    {
        DesiredScare = mood.DesiredScare;
        DesiredGore = mood.DesiredGore;
        DesiredDread = mood.DesiredDread;
        Novelty = mood.Novelty;
        PreferSlashers = mood.PreferSlashers;
        PreferExtreme = mood.PreferExtreme;
        RecommendationCount = mood.RecommendationCount;
    }

    private async Task SyncPlexAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            using var cancellation = new CancellationTokenSource();
            _plexSyncCancellation = cancellation;
            CancelPlexSyncCommand.RaiseCanExecuteChanged();
            await SyncPlexCoreAsync(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            PlexStatus = "Plex synchronization was canceled. Previously committed history remains available.";
        }
        catch (Exception exception)
        {
            PlexStatus = "Plex synchronization failed; previously imported history remains available offline.";
            await SetSafeFailureAsync(exception, "plex-sync").ConfigureAwait(true);
        }
        finally
        {
            _plexSyncCancellation = null;
            CancelPlexSyncCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }

    private Task CancelPlexSyncAsync()
    {
        _plexSyncCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private async Task SyncPlexCoreAsync(CancellationToken cancellationToken = default)
    {
        PlexStatus = "Synchronizing Plex watch history...";
        PlexSyncResult result = await _plexSyncService.SyncAsync(
            PlexConnectionId,
            cancellationToken).ConfigureAwait(true);
        await MergePlexHistoryAsync().ConfigureAwait(true);
        UpdateLocalProfileSummary();
        RefreshTasteSignals();
        await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
        PlexStatus = $"Plex sync complete: {result.ImportedCount:N0} new, {result.DuplicateCount:N0} already imported.";
    }

    private async Task MergePlexHistoryAsync()
    {
        HashSet<string> plexKeys = await _userDataStore.GetPlexWatchedExclusionKeysAsync().ConfigureAwait(true);
        IReadOnlyList<PlexHistoryItem> plexHistory = await _userDataStore.GetPlexHistoryAsync().ConfigureAwait(true);
        PlexHistoryProjection projection = _plexHistoryProjectionService.Project(
            _allCandidates,
            _tasteSource,
            _watchedExclusionKeys,
            plexKeys,
            plexHistory);
        _tasteSource = projection.TasteSignals;
        _watchedExclusionKeys = projection.WatchedExclusionKeys;
    }

    private void UpdateLocalProfileSummary()
    {
        int rawEvents = _tasteSource.Sum(static title => title.RawEventCount);
        int movieIdentities = _allCandidates.Count;
        int coreHorrorTitles = _allCandidates.Count(static candidate =>
            string.Equals(candidate.ScopeTier, "Core Horror", StringComparison.OrdinalIgnoreCase));
        Stats = new DashboardStats(
            rawEvents,
            movieIdentities,
            _tasteSource.Count,
            coreHorrorTitles,
            movieIdentities,
            0,
            0,
            0);
    }

    private void RefreshTasteSignals()
    {
        TasteSignals.Clear();
        foreach (TitleCatalogItem title in _tasteSource
                     .Where(static title => !title.NeedsReview)
                     .OrderByDescending(static title => title.WeightedTasteEventCount)
                     .ThenBy(static title => title.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            TasteSignals.Add(title);
        }
    }

    private Task RefreshRecommendationsCoreAsync()
    {
        string? selectedCandidateId = SelectedFilmDetails?.Candidate.CandidateId;
        RecommendationQueryResult queryResult = _recommendationQueryService.Execute(
            new RecommendationQuery(
            _allCandidates,
            _tasteSource,
            _feedback,
            _watchedExclusionKeys,
            DesiredScare,
            DesiredGore,
            DesiredDread,
            PreferSlashers,
            PreferExtreme,
            Novelty,
            RecommendationCount,
            string.Empty,
            SelectedSort));

        IReadOnlyList<RecommendationResult> visibleResults = queryResult.Results;
        int totalMatchingCount = visibleResults.Count;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            CatalogSearchOutcome search = _catalogSearchService.Search(
                _feedback,
                SearchText,
                RecommendationCount);
            SearchDiagnostic = search.Diagnostic is null
                ? null
                : $"{search.Diagnostic.StableCode}: check the query near character {search.Diagnostic.Span.Start + 1}.";
            totalMatchingCount = search.TotalMatchingCount;
            if (search.Diagnostic is null)
            {
                var recommendationsById = queryResult.Results.ToDictionary(
                    static result => result.Candidate.CandidateId,
                    StringComparer.Ordinal);
                visibleResults = search.Results
                    .Select(result => recommendationsById.TryGetValue(
                        result.CandidateId,
                        out RecommendationResult? recommendation)
                        ? recommendation
                        : CreateSearchRecommendation(result))
                    .ToArray();
            }
            else
            {
                visibleResults = Array.Empty<RecommendationResult>();
            }
        }
        else
        {
            SearchDiagnostic = null;
        }

        Recommendations.Clear();
        var posterCancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation = Interlocked.Exchange(
            ref _posterLoadCancellation,
            posterCancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        foreach (RecommendationResult result in visibleResults)
        {
            _feedback.TryGetValue(result.Candidate.CandidateId, out var feedback);
            var card = new RecommendationCardViewModel(result, feedback);
            Recommendations.Add(card);
            _ = card.LoadPosterAsync(_posterImageProvider, posterCancellation.Token);
        }

        if (selectedCandidateId is not null)
        {
            RecommendationCardViewModel? selectedCard = Recommendations.FirstOrDefault(card =>
                string.Equals(
                    card.Candidate.CandidateId,
                    selectedCandidateId,
                    StringComparison.OrdinalIgnoreCase));
            SelectedFilmDetails = selectedCard is null
                ? null
                : CreateFilmDetails(selectedCard);
        }

        OnPropertyChanged(nameof(ResultsSummary));

        StatusText = SearchDiagnostic ??
            $"Showing {Recommendations.Count:N0} of {totalMatchingCount:N0} matching films from " +
            $"{_allCandidates.Count:N0} curated candidates. " +
            $"{queryResult.WatchedExclusionCount:N0} catalog titles were blocked by watch-history identity matching.";

        return Task.CompletedTask;
    }

    private FilmDetailsViewModel CreateFilmDetails(RecommendationCardViewModel card)
    {
        var details = new FilmDetailsViewModel(
            card,
            _posterImageProvider,
            _posterCustomizationStore);
        _ = details.PosterStudio.InitializeAsync();
        return details;
    }

    private static RecommendationResult CreateSearchRecommendation(
        HorrorHarbor.Search.Contracts.SearchResult result) => new(
        result.Candidate,
        result.HybridScore,
        result.IsFilterOnly
            ? "Matches every selected catalog filter."
            : $"Catalog match: {result.LexicalTier}.",
        new RecommendationScoreBreakdown(
            result.LexicalScore.Total,
            0,
            0,
            0,
            result.DiscoveryScore,
            result.QualityScore,
            0,
            result.AffinityScore));

    private async Task ApplyFeedbackAsync(
        RecommendationCardViewModel card,
        RecommendationFeedback feedback)
    {
        try
        {
            _feedback.TryGetValue(card.Candidate.CandidateId, out RecommendationFeedback previous);
            FeedbackChange change = await _feedbackCoordinator.ApplyAsync(
                card.Candidate.CandidateId,
                previous,
                feedback).ConfigureAwait(true);
            card.Feedback = feedback;
            _feedback[card.Candidate.CandidateId] = feedback;
            _lastFeedbackChange = change;
            UndoFeedbackCommand.RaiseCanExecuteChanged();
            await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await SetSafeFailureAsync(exception, "feedback-save").ConfigureAwait(true);
        }
    }

    private async Task UndoFeedbackAsync()
    {
        if (_lastFeedbackChange is not { } change)
        {
            return;
        }

        FeedbackUndoResult result = await _feedbackCoordinator.UndoAsync(change).ConfigureAwait(true);
        if (!result.WasUndone)
        {
            _lastFeedbackChange = null;
            UndoFeedbackCommand.RaiseCanExecuteChanged();
            StatusText = "The latest feedback could not be undone because its persisted history changed.";
            return;
        }

        if (result.Current == RecommendationFeedback.None)
        {
            _feedback.Remove(change.CandidateId);
        }
        else
        {
            _feedback[change.CandidateId] = result.Current;
        }

        _lastFeedbackChange = null;
        UndoFeedbackCommand.RaiseCanExecuteChanged();
        await RefreshRecommendationsCoreAsync().ConfigureAwait(true);
        StatusText = "The latest recommendation feedback was undone.";
    }

    private void HandleCommandError(Exception exception) =>
        _ = SetSafeFailureAsync(exception, "command-execution");

    private async Task SetSafeFailureAsync(Exception exception, string operation)
    {
        string reference = await _errorReporter.ReportAsync(exception, operation).ConfigureAwait(true);
        StatusText = $"The operation failed safely. Reference: {reference}";
    }
}
