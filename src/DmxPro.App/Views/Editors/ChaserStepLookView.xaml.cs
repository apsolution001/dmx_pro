using System.Windows;
using System.Windows.Controls;
using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Step-look panel of the chaser editor (lights, colours, levels, all channels of one own-look step). Code-behind only
/// tells the ViewModel how to open the modal Choose lights dialog over this panel's window (the editors are created
/// without DI, so they have no dialog service), and offers <see cref="HeaderContent"/> so the host can put its own
/// controls (e.g. the "Lights show: This step / Whole chaser" switch) in the panel header.
/// DataContext: <see cref="ChaserStepLookViewModel"/>.
/// </summary>
public partial class ChaserStepLookView : UserControl
{
    /// <summary>Identifies the <see cref="HeaderContent"/> dependency property.</summary>
    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent),
        typeof(object),
        typeof(ChaserStepLookView),
        new PropertyMetadata(null));

    public ChaserStepLookView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Extra content shown at the right of the panel header (set by the host editor view).</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChaserStepLookViewModel old && old.ShowPicker == ShowPicker)
        {
            old.ShowPicker = null;
        }

        if (e.NewValue is ChaserStepLookViewModel viewModel)
        {
            viewModel.ShowPicker = ShowPicker;
        }
    }

    private bool ShowPicker(FixturePickerViewModel picker) => FixturePickerWindow.ShowModal(picker, Window.GetWindow(this));
}
