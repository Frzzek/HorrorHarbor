using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HorrorHarbor.App.ViewModels;

namespace HorrorHarbor.App.Views;

internal sealed class InteractiveUserTestHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly Panel _root;
    private readonly InteractiveUserTestViewModel _viewModel;
    private readonly InteractiveUserTestView _view;
    private readonly Dictionary<UIElement, bool> _underlyingEnabledStates;
    private bool _isDisposed;

    private InteractiveUserTestHost(
        MainWindow window,
        Panel root,
        InteractiveUserTestViewModel viewModel)
    {
        _window = window;
        _root = root;
        _viewModel = viewModel;
        _underlyingEnabledStates = [];
        _view = new InteractiveUserTestView
        {
            DataContext = viewModel,
        };
        Panel.SetZIndex(_view, 1000);
        _root.Children.Add(_view);
        UpdatePresentationBounds();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CloseRequested += OnCloseRequested;
        _window.Closed += OnWindowClosed;
    }

    public static InteractiveUserTestHost Attach(
        MainWindow window,
        InteractiveUserTestViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);
        if (window.Content is not Panel root)
        {
            throw new InvalidOperationException(
                "The interactive usability test requires the application shell panel.");
        }

        return new InteractiveUserTestHost(window, root, viewModel);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CloseRequested -= OnCloseRequested;
        _window.Closed -= OnWindowClosed;
        RestoreUnderlyingInteraction();
        _root.Children.Remove(_view);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InteractiveUserTestViewModel.Stage) &&
            !_isDisposed)
        {
            UpdatePresentationBounds();
            if (_viewModel.Stage == InteractiveUserTestStage.Tasks)
            {
                _ = _window.Dispatcher.InvokeAsync(
                    FocusFirstRealTask,
                    DispatcherPriority.Loaded);
            }
        }
    }

    private void UpdatePresentationBounds()
    {
        bool isTaskGuide = _viewModel.Stage == InteractiveUserTestStage.Tasks;
        if (_viewModel.IsModalStage)
        {
            DisableUnderlyingInteraction();
        }
        else
        {
            RestoreUnderlyingInteraction();
        }

        _view.HorizontalAlignment = isTaskGuide
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Stretch;
        _view.VerticalAlignment = isTaskGuide
            ? VerticalAlignment.Top
            : VerticalAlignment.Stretch;
        _view.Width = isTaskGuide ? 470 : double.NaN;
        _view.Height = double.NaN;
    }

    private void DisableUnderlyingInteraction()
    {
        if (_underlyingEnabledStates.Count == 0)
        {
            foreach (UIElement child in _root.Children.OfType<UIElement>())
            {
                if (!ReferenceEquals(child, _view))
                {
                    _underlyingEnabledStates.Add(child, child.IsEnabled);
                }
            }
        }

        foreach (UIElement child in _underlyingEnabledStates.Keys)
        {
            child.IsEnabled = false;
        }
    }

    private void RestoreUnderlyingInteraction()
    {
        foreach ((UIElement child, bool wasEnabled) in _underlyingEnabledStates)
        {
            child.IsEnabled = wasEnabled;
        }

        _underlyingEnabledStates.Clear();
    }

    private void FocusFirstRealTask()
    {
        if (_isDisposed || _viewModel.Stage != InteractiveUserTestStage.Tasks)
        {
            return;
        }

        if (TryFocusTarget(FindDescendant<OnboardingView>(_window), static onboarding => onboarding.FocusPrimaryAction()))
        {
            return;
        }

        if (TryFocusTarget(FindNamedDescendant<Control>(_window, "SearchBox"), static search => search.Focus()))
        {
            return;
        }
    }

    private static bool TryFocusTarget<T>(T? target, Func<T, bool> focusAction)
        where T : class
    {
        if (target is null)
        {
            return false;
        }

        if (target is FrameworkElement element &&
            (!element.Focusable || !element.IsEnabled || !element.IsVisible))
        {
            return false;
        }

        return focusAction(target);
    }

    private void OnCloseRequested(
        object? sender,
        InteractiveUserTestCloseRequestedEventArgs e)
    {
        if (e.ExitApplication)
        {
            _window.Close();
            return;
        }

        Dispose();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                return typed;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindNamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T { Name: var childName } typed &&
                childName.Equals(name, StringComparison.Ordinal))
            {
                return typed;
            }

            if (FindNamedDescendant<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
