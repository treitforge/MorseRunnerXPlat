using System.Windows.Input;

namespace MorseRunner.App.ViewModels;

public sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<Task> _execute =
        execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? _canExecute = canExecute;
    private int _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _isExecuting == 0 && (_canExecute?.Invoke() ?? true);
    }

    void ICommand.Execute(object? parameter)
    {
        _ = ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        Interlocked.Exchange(ref _isExecuting, 1);
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
