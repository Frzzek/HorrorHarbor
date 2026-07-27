using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.App.ViewModels;

namespace HorrorHarbor.App;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> AllowedExternalLinks = new(StringComparer.Ordinal)
    {
        "https://developer.plex.tv/",
        "https://www.plex.tv/about/privacy-legal/plex-trademarks-and-guidelines/",
        "https://www.themoviedb.org/about/logos-attribution",
        "https://www.themoviedb.org/api-terms-of-use",
    };

    private IInputElement? _detailsFocusReturnTarget;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowChromeService.Apply(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync().ConfigureAwait(true);
    }

    private async void OnConnectPlex(object sender, RoutedEventArgs e)
    {
        string token = PlexTokenBox.Password;
        PlexTokenBox.Clear();
        await ViewModel.ConnectPlexAsync(token).ConfigureAwait(true);
    }

    private async void OnSaveTmdbToken(object sender, RoutedEventArgs e)
    {
        if (TmdbPersonalUseAcknowledgement.IsChecked != true)
        {
            TmdbTokenBox.Clear();
            MessageBox.Show(
                "Official TMDB artwork remains disabled. Confirm that you are using your own authorized credential for personal, non-commercial use and accept TMDB's current terms.",
                "TMDB acknowledgement required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string token = TmdbTokenBox.Password;
        TmdbTokenBox.Clear();
        await ViewModel.SaveTmdbTokenAsync(token).ConfigureAwait(true);
    }

    private async void OnDeleteTmdbToken(object sender, RoutedEventArgs e)
    {
        TmdbTokenBox.Clear();
        await ViewModel.DeleteTmdbTokenAsync().ConfigureAwait(true);
    }

    private void OnOpenExternalLink(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is not { IsAbsoluteUri: true, Scheme: "https" } uri ||
            !AllowedExternalLinks.Contains(uri.AbsoluteUri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                "Windows could not open the approved external link.",
                "Nocturne Harbor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Handled = true;
        }
    }

    private void OnViewDetails(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecommendationCardViewModel card } element)
        {
            return;
        }

        _detailsFocusReturnTarget = element;
        ViewModel.OpenFilmDetails(card);
        Dispatcher.BeginInvoke(DetailsCloseButton.Focus);
    }

    private void OnCloseDetails(object sender, RoutedEventArgs e) => CloseDetails();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsFilmDetailsOpen)
        {
            CloseDetails();
            e.Handled = true;
        }
    }

    private void CloseDetails()
    {
        ViewModel.CloseFilmDetails();
        IInputElement? focusTarget = _detailsFocusReturnTarget;
        _detailsFocusReturnTarget = null;
        if (focusTarget is not null)
        {
            Dispatcher.BeginInvoke(() => Keyboard.Focus(focusTarget));
        }
    }

    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        e.Handled = true;
    }
}
