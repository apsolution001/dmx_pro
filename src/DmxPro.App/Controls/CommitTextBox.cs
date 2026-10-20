using System.Windows.Controls;
using System.Windows.Input;

namespace DmxPro.App.Controls;

/// <summary>
/// Compact text box for a value that is validated by the ViewModel (e.g. seconds in a step grid): bind
/// <c>Text</c> with the default LostFocus trigger. Enter commits (the ViewModel clamps or reverts and the box shows the
/// stored value), Esc throws the typing away, and focusing selects all so typing replaces the value. Esc with nothing
/// typed is left to the parent (e.g. closing the editor).
/// </summary>
public sealed class CommitTextBox : TextBox
{
    /// <summary>Initializes a new instance of the <see cref="CommitTextBox"/> class with the app's TextBox style.</summary>
    public CommitTextBox()
    {
        // Implicit styles are looked up by exact type; take the themed TextBox style (dark + light).
        SetResourceReference(StyleProperty, typeof(TextBox));
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var binding = GetBindingExpression(TextProperty);
        switch (e.Key)
        {
            case Key.Enter when !AcceptsReturn:
                binding?.UpdateSource();
                binding?.UpdateTarget();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Escape when binding is not null && binding.IsDirty:
                binding.UpdateTarget();
                SelectAll();
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);

        // Commit now (also when focus leaves the window), then show what the ViewModel kept (clamped or reverted).
        var binding = GetBindingExpression(TextProperty);
        if (binding is { IsDirty: true })
        {
            binding.UpdateSource();
        }

        binding?.UpdateTarget();
    }
}
