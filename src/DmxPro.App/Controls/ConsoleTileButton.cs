using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DmxPro.App.Controls;

/// <summary>
/// Button for console tiles that also reports press and release, for flash (momentary) widgets. WPF's
/// <see cref="System.Windows.Controls.Primitives.ButtonBase.IsPressed"/> already goes false on mouse/touch up, lost
/// mouse capture and Space key up, so a flash released by any of those always gets <see cref="ReleaseCommand"/>.
/// </summary>
public sealed class ConsoleTileButton : Button
{
    public static readonly DependencyProperty PressCommandProperty = DependencyProperty.Register(
        nameof(PressCommand), typeof(ICommand), typeof(ConsoleTileButton), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.Register(
        nameof(ReleaseCommand), typeof(ICommand), typeof(ConsoleTileButton), new PropertyMetadata(null));

    /// <summary>Executed when the button becomes pressed.</summary>
    public ICommand? PressCommand
    {
        get => (ICommand?)GetValue(PressCommandProperty);
        set => SetValue(PressCommandProperty, value);
    }

    /// <summary>Executed when the button stops being pressed (for any reason).</summary>
    public ICommand? ReleaseCommand
    {
        get => (ICommand?)GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        var command = (bool)e.NewValue ? PressCommand : ReleaseCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);

        // Focus moving to another control in the app while Space is held ends a flash (safety rule 5). Focus leaving the
        // app (NewFocus null: switching to another program) keeps it on, like every other light (owner decision 2026-10-22).
        if (e.NewFocus is not null && ReleaseCommand?.CanExecute(null) == true)
        {
            ReleaseCommand.Execute(null);
        }
    }
}
