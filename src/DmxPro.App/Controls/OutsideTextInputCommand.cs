using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DmxPro.App.Controls;

/// <summary>
/// Wraps a command for a window-level keyboard shortcut that must not act while the user types: with a text box focused
/// (editable combo boxes and grid cells included) it is disabled, so the key reaches the text box unhandled-by-us and,
/// for Ctrl+Z / Ctrl+Y, the box keeps its own text undo. Menu items and toolbar buttons bind the plain command.
/// </summary>
public sealed class OutsideTextInputCommand : ICommand
{
    private readonly ICommand _inner;

    public OutsideTextInputCommand(ICommand inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => _inner.CanExecuteChanged += value;
        remove => _inner.CanExecuteChanged -= value;
    }

    /// <summary>True while keyboard focus is in a text input.</summary>
    public static bool IsTextInputFocused() => Keyboard.FocusedElement is TextBoxBase or PasswordBox;

    public bool CanExecute(object? parameter) => !IsTextInputFocused() && _inner.CanExecute(parameter);

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _inner.Execute(parameter);
        }
    }
}
