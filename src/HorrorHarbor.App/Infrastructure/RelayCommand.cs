using System.Windows.Input;

namespace HorrorHarbor.App.Infrastructure;

public sealed class RelayCommand<T>(
    Action<T> execute,
    Predicate<T>? canExecute = null) : ICommand
    where T : class
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T typed && (canExecute?.Invoke(typed) ?? true);

    public void Execute(object? parameter)
    {
        if (parameter is T typed && CanExecute(typed))
        {
            execute(typed);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
