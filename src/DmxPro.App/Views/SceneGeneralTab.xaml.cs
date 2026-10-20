using System.Windows.Controls;
using System.Windows.Input;

namespace DmxPro.App.Views;

/// <summary>
/// General tab of the scene editor (which lights the scene uses). Code-behind only moves keyboard focus into and out
/// of the Add flyout (pure view concern); all state lives in <see cref="ViewModels.SceneGeneralTabViewModel"/>.
/// </summary>
public partial class SceneGeneralTab : UserControl
{
    public SceneGeneralTab()
    {
        InitializeComponent();
    }

    /// <summary>A popup does not take focus by itself; put it on the flyout's search box so typing filters at once.</summary>
    private void OnAddPopupOpened(object? sender, EventArgs e)
    {
        AddPopupBody.Dispatcher.BeginInvoke(() =>
            AddPopupBody.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

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
