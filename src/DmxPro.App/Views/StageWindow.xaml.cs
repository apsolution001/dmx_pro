using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App.Views;

/// <summary>
/// Live stage window: the stage plan of <see cref="StagePageViewModel"/> in its own non-modal window. Opening, the
/// single shared instance and the viewer registration live in <see cref="Services.StageWindowService"/>.
/// </summary>
public partial class StageWindow : FluentWindow
{
    public StageWindow(StagePageViewModel viewModel, ShellViewModel shell)
    {
        DataContext = viewModel;
        InitializeComponent();
        AddSafetyShortcuts(shell);

        // Window chrome only (view concern): Ctrl+W closes, like other secondary windows.
        Add(new RelayCommand(Close), Key.W, ModifierKeys.Control);
        Loaded += OnLoaded;
    }

    /// <summary>Puts keyboard focus on the stage content (its first focusable control) so the window is keyboard
    /// usable at once instead of starting with nothing focused.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        StageContent.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    /// <summary>
    /// The live safety shortcuts of the main window (and Ctrl+S) also work while the stage window has focus: an operator
    /// watching the stage must reach Blackout / Release all / output off without first switching windows. Bound to the
    /// shell's own commands, so behaviour (confirmations, state) is identical to the main window. The DataContext here
    /// is the stage ViewModel, hence direct command references instead of XAML bindings.
    /// </summary>
    private void AddSafetyShortcuts(ShellViewModel shell)
    {
        Add(shell.ToggleBlackoutCommand, Key.B, ModifierKeys.Control);
        Add(shell.ReleaseAllCommand, Key.Escape, ModifierKeys.Shift);
        Add(shell.InputOutput.EnableOutputCommand, Key.E, ModifierKeys.Control);
        Add(shell.InputOutput.DisableOutputCommand, Key.E, ModifierKeys.Control | ModifierKeys.Shift);
        Add(shell.ToggleLiveModeCommand, Key.F5, ModifierKeys.Control);
        Add(shell.SaveProjectCommand, Key.S, ModifierKeys.Control);
    }

    private void Add(ICommand command, Key key, ModifierKeys modifiers) =>
        InputBindings.Add(new KeyBinding(command, key, modifiers));
}
