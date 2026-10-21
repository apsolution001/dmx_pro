using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App.Views;

/// <summary>
/// Live stage window: the stage plan of <see cref="StagePageViewModel"/> in its own non-modal window. Opening, the
/// single shared instance and the viewer registration live in <see cref="Services.StageWindowService"/>.
/// </summary>
public partial class StageWindow : FluentWindow
{
    public StageWindow(StagePageViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
