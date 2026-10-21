using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DmxPro.App.Controls;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Stage page: the lights list and the stage plan (<see cref="StageCanvas"/>). Code-behind only does view behaviour:
/// opening the Arrange menu, starting a drag from the lights list onto the stage and hiding the lights list.
/// </summary>
/// <remarks>Also hosted by the live stage window (<see cref="StageWindow"/>), there without the lights list.</remarks>
public partial class StageView : UserControl
{
    /// <summary>Identifies the <see cref="ShowLightList"/> dependency property.</summary>
    public static readonly DependencyProperty ShowLightListProperty = DependencyProperty.Register(
        nameof(ShowLightList),
        typeof(bool),
        typeof(StageView),
        new PropertyMetadata(true, OnShowLightListChanged));

    private GridLength _lightListWidth = new(240);
    private Point _dragStart;
    private StageLightViewModel? _dragLight;

    public StageView()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets whether the lights list (left panel) is shown. The live stage window turns it off.</summary>
    public bool ShowLightList
    {
        get => (bool)GetValue(ShowLightListProperty);
        set => SetValue(ShowLightListProperty, value);
    }

    private static void OnShowLightListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (StageView)d;
        var show = (bool)e.NewValue;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        view.LightListPanel.Visibility = visibility;
        view.LightListSplitter.Visibility = visibility;
        if (show)
        {
            view.LightListColumn.MinWidth = 160;
            view.LightListColumn.Width = view._lightListWidth;
            view.LightListSplitterColumn.Width = new GridLength(4);
        }
        else
        {
            // Keep a width the user dragged with the splitter, for when the list comes back.
            view._lightListWidth = view.LightListColumn.Width;
            view.LightListColumn.MinWidth = 0;
            view.LightListColumn.Width = new GridLength(0);
            view.LightListSplitterColumn.Width = new GridLength(0);
        }
    }

    /// <summary>The toolbar "Arrange" button opens its menu below itself on a normal click.</summary>
    private void OnArrangeMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnLightListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(LightList);
        _dragLight = FindLight(e.OriginalSource as DependencyObject);
    }

    private void OnLightListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragLight is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragLight = null;
            return;
        }

        var point = e.GetPosition(LightList);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var light = _dragLight;
        _dragLight = null;
        if (light.IsDrawable)
        {
            DragDrop.DoDragDrop(LightList, new DataObject(StageCanvas.LightDragFormat, light.FixtureId), DragDropEffects.Move);
        }
    }

    private static StageLightViewModel? FindLight(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem { DataContext: StageLightViewModel light })
            {
                return light;
            }

            if (source is ScrollBar)
            {
                return null;
            }

            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }
}
