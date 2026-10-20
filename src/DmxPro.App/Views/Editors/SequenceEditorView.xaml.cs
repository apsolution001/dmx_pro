using System.Windows.Controls;
using System.Windows.Input;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Sequence editor (fixtures, steps, step values). Code-behind only selects a step row when one of its boxes gets
/// focus (pure view concern). DataContext: <c>SequenceEditorViewModel</c>.
/// </summary>
public partial class SequenceEditorView : UserControl
{
    public SequenceEditorView()
    {
        InitializeComponent();
    }

    /// <summary>Typing in a step's box selects that step, so its values show on the right.</summary>
    private void OnRowGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DataGridRow { IsSelected: false } row)
        {
            row.IsSelected = true;
        }
    }
}
