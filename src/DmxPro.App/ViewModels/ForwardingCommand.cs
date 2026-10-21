using System.Windows.Input;

namespace DmxPro.App.ViewModels;

/// <summary>
/// A shell menu / toolbar command that runs a page's own command (e.g. Edit ▸ Delete → the current page's Delete). It is
/// enabled exactly when the resolved page command can run, so a page without that action shows the menu item disabled.
/// <paramref name="beforeExecute"/> can navigate to the page first. Every command the resolver can return must be in
/// <paramref name="watched"/> so their enabled-state changes reach the menu; call <see cref="NotifyCanExecuteChanged"/>
/// when the resolver's answer changes for another reason (e.g. the current page changed).
/// </summary>
public sealed class ForwardingCommand : ICommand
{
    private readonly Func<ICommand?> _resolve;
    private readonly Action? _beforeExecute;

    public ForwardingCommand(Func<ICommand?> resolve, Action? beforeExecute, params ICommand[] watched)
    {
        _resolve = resolve;
        _beforeExecute = beforeExecute;
        foreach (var command in watched)
        {
            command.CanExecuteChanged += (_, _) => NotifyCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _resolve()?.CanExecute(parameter) == true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _beforeExecute?.Invoke();

        // Navigating can change what the page offers (it reloads on arrival), so ask again.
        if (_resolve() is { } command && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
