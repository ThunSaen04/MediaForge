using System.Windows.Input;
using MediaForge.Core;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action action, Func<bool>? can = null)
        : this(_ => action(), can is null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>Async command that disables itself while running and reports failures in a dialog.</summary>
public sealed class AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _running;

    public AsyncCommand(Func<Task> action, Func<bool>? can = null)
        : this(_ => action(), can is null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute(parameter);
        }
        catch (OperationCanceledException)
        {
            // User cancelled.
        }
        catch (UserException ex)
        {
            Dialogs.Error(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.ToString());
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
