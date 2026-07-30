using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Data.Catalog;
using HorrorHarbor.Data.Feedback;
using HorrorHarbor.Data.Plex;
using HorrorHarbor.Data.UserData;
using HorrorHarbor.Posters;
using HorrorHarbor.Recommendations.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HorrorHarbor.App;

public partial class App : Application
{
    private IHost? _host;
    private SecureLocalErrorReporter? _errorReporter;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var errorReporter = new SecureLocalErrorReporter();
        _errorReporter = errorReporter;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyColorMode();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<ICandidateCatalog>(
                        new JsonCandidateCatalog(CatalogPathResolver.Resolve()));
                    services.AddSingleton<IFeedbackStore, JsonFeedbackStore>();
                    services.AddSingleton<
                        IUserDataEncryptionKeyProvider,
                        DpapiUserDataEncryptionKeyProvider>();
                    services.AddSingleton<IUserDataStore, SqliteUserDataStore>();
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IFeedbackCoordinator, FeedbackCoordinator>();
                    services.AddSingleton<WindowsCredentialPlexCredentialStore>();
                    services.AddSingleton<DpapiPlexCredentialStore>();
                    services.AddSingleton<IPlexCredentialStore, FallbackPlexCredentialStore>();
                    services.AddSingleton(new HttpClient(new HttpClientHandler
                    {
                        AllowAutoRedirect = false,
                    })
                    {
                        Timeout = TimeSpan.FromSeconds(30),
                    });
                    services.AddSingleton<IPlexClient, PlexClient>();
                    services.AddSingleton<IPlexSyncService, PlexSyncService>();
                    services.AddSingleton<ISecureErrorReporter>(errorReporter);
                    services.AddSingleton<IRecommendationEngine, DeterministicRecommendationEngine>();
                    services.AddSingleton<RecommendationQueryService>();
                    services.AddSingleton<ICatalogSearchService, CatalogSearchService>();
                    services.AddSingleton(new DiscoveryMoodService(new Random()));
                    services.AddSingleton<
                        IPlexHistoryProjectionService,
                        PlexHistoryProjectionService>();
                    services.AddSingleton(PosterCacheOptions.CreateDefault());
                    services.AddSingleton(TmdbPosterOptions.CreateDefault());
                    services.AddSingleton<IPosterCredentialStore, WindowsCredentialPosterCredentialStore>();
                    services.AddSingleton<LocalPosterService>();
                    services.AddSingleton<TmdbPosterService>();
                    services.AddSingleton<IPosterService, HybridPosterService>();
                    services.AddSingleton<IPosterImageProvider, PosterImageProvider>();
                    services.AddSingleton<ViewModels.MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync().ConfigureAwait(true);
            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            string reference = await errorReporter.ReportAsync(exception, "application-startup").ConfigureAwait(true);
            MessageBox.Show(
                $"Nocturne Harbor could not start safely. Reference: {reference}",
                "Nocturne Harbor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private async void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        string reference = _errorReporter is null
            ? "UNAVAILABLE"
            : await _errorReporter.ReportAsync(e.Exception, "dispatcher-unhandled").ConfigureAwait(true);
        MessageBox.Show(
            $"Nocturne Harbor recovered from an unexpected error. Reference: {reference}",
            "Nocturne Harbor",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            ApplyColorMode();
            if (MainWindow is { } window)
            {
                WindowChromeService.Apply(window);
            }
        }
    }

    private void ApplyColorMode()
    {
        if (SystemParameters.HighContrast)
        {
            Resources["WindowBrush"] = SystemColors.WindowBrush;
            Resources["SidebarBrush"] = SystemColors.WindowBrush;
            Resources["PanelBrush"] = SystemColors.WindowBrush;
            Resources["PanelRaisedBrush"] = SystemColors.WindowBrush;
            Resources["PanelHoverBrush"] = SystemColors.ControlBrush;
            Resources["PanelBorderBrush"] = SystemColors.WindowTextBrush;
            Resources["PrimaryTextBrush"] = SystemColors.WindowTextBrush;
            Resources["SecondaryTextBrush"] = SystemColors.WindowTextBrush;
            Resources["AccentBrush"] = SystemColors.HighlightBrush;
            Resources["AccentSoftBrush"] = SystemColors.HighlightBrush;
            Resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
            Resources["GoldBrush"] = SystemColors.WindowTextBrush;
            Resources["PositiveBrush"] = SystemColors.HighlightBrush;
            return;
        }

        Resources["WindowBrush"] = new SolidColorBrush(Color.FromRgb(9, 11, 16));
        Resources["SidebarBrush"] = new SolidColorBrush(Color.FromRgb(14, 17, 24));
        Resources["PanelBrush"] = new SolidColorBrush(Color.FromRgb(20, 24, 33));
        Resources["PanelRaisedBrush"] = new SolidColorBrush(Color.FromRgb(25, 30, 41));
        Resources["PanelHoverBrush"] = new SolidColorBrush(Color.FromRgb(31, 38, 52));
        Resources["PanelBorderBrush"] = new SolidColorBrush(Color.FromRgb(47, 56, 72));
        Resources["PrimaryTextBrush"] = new SolidColorBrush(Color.FromRgb(247, 248, 250));
        Resources["SecondaryTextBrush"] = new SolidColorBrush(Color.FromRgb(192, 200, 212));
        Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(185, 35, 61));
        Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromRgb(66, 26, 40));
        Resources["AccentTextBrush"] = new SolidColorBrush(Colors.White);
        Resources["GoldBrush"] = new SolidColorBrush(Color.FromRgb(244, 188, 97));
        Resources["PositiveBrush"] = new SolidColorBrush(Color.FromRgb(74, 222, 128));
    }
}
