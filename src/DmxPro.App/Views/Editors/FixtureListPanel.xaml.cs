using System.Windows.Controls;
using System.Windows.Input;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Fixtures panel of the sequence and effect editors. Code-behind only moves keyboard focus into and out of the Add
/// flyout (pure view concern). DataContext: <c>FixtureListEditorViewModel</c>.
/// </summary>
public partial class FixtureListPanel : UserControl
{
    public FixtureListPanel()
    {
        InitializeComponent();
    }

    /// <summary>A popup does not take focus by itself; put it in the search box so the flyout is keyboard usable.</summary>
    private void OnAddPopupOpened(object? sender, EventArgs e) =>
        SearchBox.Dispatcher.BeginInvoke(() => SearchBox.Focus());

    /// <summary>Esc closes the flyout and returns focus to the Add button.</summary>
    private void OnAddPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AddPopup.IsOpen = false;
            AddToggle.Focus();
            e.Handled = true;
        }
    }
}
