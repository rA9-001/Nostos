using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;

namespace Nostos.App.ViewModels;

/// <summary>
/// Hand-rolled MVVM primitives.
///
/// Not CommunityToolkit.Mvvm or ReactiveUI: the product keeps its dependency surface small
/// enough to audit, and this is thirty lines against a package tree.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(propertyName);
        return true;
    }

    /// <summary>
    /// Raises PropertyChanged, marshalling to the UI thread when it is not already on it.
    ///
    /// Now that the in-process engine runs on the thread pool, a view model property can be set
    /// from a pool thread -- a background refresh landing, a step in the startup sequence -- and
    /// an Avalonia binding updated off the UI thread is an exception at best and a torn control
    /// at worst. Doing the check here means no caller has to remember, which is the only version
    /// of this rule that stays true.
    /// </summary>
    protected void Raise([CallerMemberName] string? propertyName = null)
    {
        if (PropertyChanged is not { } handler)
            return;

        var args = new PropertyChangedEventArgs(propertyName);

        if (Dispatcher.UIThread.CheckAccess())
            handler(this, args);
        else
            Dispatcher.UIThread.Post(() => handler(this, args));
    }
}

/// <summary>An <see cref="ICommand"/> over an async handler, with re-entrancy blocked.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    /// <summary>
    /// Execute, awaitable.
    ///
    /// Exists because ICommand.Execute is fire-and-forget, which leaves a test with no option
    /// but to sleep for a guessed interval and hope. Production code goes through Execute.
    /// </summary>
    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception e)
        {
            // A command handler that throws must not take down the UI thread. Handlers are
            // expected to surface their own errors; this is the backstop.
            System.Diagnostics.Debug.WriteLine(e);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
