using System.Windows;
using System.Windows.Controls;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// General tab of the scene editor (which lights the scene uses). Code-behind only tells the ViewModel how to open the
/// modal Choose lights dialog over this tab's window (the editors are created without DI); all state lives in
/// <see cref="SceneGeneralTabViewModel"/>.
/// </summary>
public partial class SceneGeneralTab : UserControl
{
    public SceneGeneralTab()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SceneGeneralTabViewModel old && old.ShowPicker == ShowPicker)
        {
            old.ShowPicker = null;
        }

        if (e.NewValue is SceneGeneralTabViewModel viewModel)
        {
            viewModel.ShowPicker = ShowPicker;
        }
    }

    private bool ShowPicker(FixturePickerViewModel picker) => FixturePickerWindow.ShowModal(picker, Window.GetWindow(this));
}
