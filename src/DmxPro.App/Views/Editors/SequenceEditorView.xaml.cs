using System.Windows.Controls;
using System.Windows.Input;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Sequence editor (Lights, Steps, Playback). Code-behind only selects a step row when one of its boxes gets focus
/// (pure view concern). DataContext: <c>SequenceEditorViewModel</c>.
/// </summary>
public partial class SequenceEditorView : UserControl
{
    public SequenceEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Typing in a step's box makes that step the current one, so its colours show on the right. Without Ctrl/Shift
    /// it becomes the only selected step, as a click on the row would.
    /// </summary>
    private void OnRowGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not DataGridRow { IsSelected: false } row)
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
        {
            StepsGrid.SelectedItem = row.Item;
        }
        else
        {
            row.IsSelected = true;
        }
    }
}
