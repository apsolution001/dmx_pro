using System.Windows;
using System.Windows.Controls;
using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Lights panel of the sequence and effect editors. Code-behind only tells the ViewModel how to open the modal
/// Choose lights dialog over this panel's window (the editors are created without DI, so they have no dialog service).
/// DataContext: <see cref="FixtureListEditorViewModel"/>.
/// </summary>
public partial class FixtureListPanel : UserControl
{
    public FixtureListPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FixtureListEditorViewModel old && old.ShowPicker == ShowPicker)
        {
            old.ShowPicker = null;
        }

        if (e.NewValue is FixtureListEditorViewModel viewModel)
        {
            viewModel.ShowPicker = ShowPicker;
        }
    }

    private bool ShowPicker(FixturePickerViewModel picker) => FixturePickerWindow.ShowModal(picker, Window.GetWindow(this));
}
