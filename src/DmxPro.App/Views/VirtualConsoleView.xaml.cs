using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DmxPro.App.Controls;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Virtual Console view. Code-behind holds only view concerns: pointer drag/resize on the grid, palette drag-and-drop,
/// keyboard routing, cell sizing, and releasing held flashes when the window deactivates or the view unloads
/// (safety rule 5). All state changes go through <see cref="VirtualConsoleViewModel"/>.
/// </summary>
public partial class VirtualConsoleView : UserControl
{
    private static readonly string PaletteDragFormat = "DmxPro.ConsoleWidgetKind";
    private static readonly double SurfaceMargin = 8;

    private Window? _window;
    private VirtualConsoleViewModel? _subscribed;
    private ConsoleWidgetViewModel? _dragWidget;
    private Point _dragStart;
    private double _dragStartA;
    private double _dragStartB;
    private bool _resizing;
    private Point? _paletteDragStart;

    public VirtualConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private VirtualConsoleViewModel? Vm => DataContext as VirtualConsoleViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Deactivated += OnWindowDeactivated;
        }

        UpdateCellSize();
        FocusCanvas();
    }

    /// <summary>
    /// The page host keeps this view alive and only collapses it when another page is shown (no Unloaded then), so
    /// leaving the page ends drags and held flashes here (safety rule 5) and coming back takes keyboard focus again.
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            FocusCanvas();
            return;
        }

        EndDrag();
        Vm?.ReleaseFlashes();
    }

    /// <summary>
    /// Takes keyboard focus so console key shortcuts work right away, unless the user is moving through a list (e.g. the
    /// navigation rail) with the keyboard.
    /// </summary>
    private void FocusCanvas()
    {
        if (!IsKeyboardFocusWithin && Keyboard.FocusedElement is not ListBoxItem)
        {
            Dispatcher.BeginInvoke(() => CanvasHost.Focus(), DispatcherPriority.Input);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }

        EndDrag();
        Vm?.ReleaseFlashes();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Vm?.ReleaseFlashes();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = Vm;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateCellSize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VirtualConsoleViewModel.SelectedPage))
        {
            UpdateCellSize();
        }
    }

    // ───────────────────────────── Keyboard ─────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.F12 && modifiers == ModifierKeys.None)
        {
            vm.ToggleOperatorModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is TextBoxBase)
        {
            return;
        }

        // Space = show play/pause in the console. A focused button (console tile, toolbar, transport) keeps Space for
        // its own click so tiles stay keyboard-operable; the edit panel's combo boxes keep it too.
        if (key == Key.Space && modifiers == ModifierKeys.None && e.OriginalSource is not ButtonBase
            && (!vm.IsEditMode || CanvasHost.IsKeyboardFocusWithin || TransportBar.IsKeyboardFocusWithin))
        {
            e.Handled = e.IsRepeat || TogglePlayPause(vm.ShowPlayer);
            return;
        }

        if (vm.IsEditMode && vm.HasSelectedWidget)
        {
            if (key == Key.Delete && modifiers == ModifierKeys.None)
            {
                vm.DeleteWidgetCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (key == Key.D && modifiers == ModifierKeys.Control)
            {
                vm.DuplicateWidgetCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (CanvasHost.IsKeyboardFocusWithin && modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                var (dx, dy) = key switch
                {
                    Key.Left => (-1, 0),
                    Key.Right => (1, 0),
                    Key.Up => (0, -1),
                    Key.Down => (0, 1),
                    _ => (0, 0),
                };
                if ((dx != 0 || dy != 0) && vm.NudgeSelected(dx, dy, modifiers == ModifierKeys.Shift))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        if (key == Key.F2 && modifiers == ModifierKeys.None && vm.ShowEditChrome && vm.HasSelectedPage)
        {
            vm.RenamePageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!vm.IsEditMode && modifiers == ModifierKeys.None && ConsoleKeyNames.From(key) is { } name)
        {
            // Auto-repeat must not re-toggle a button; still swallow it so it doesn't reach other controls.
            e.Handled = e.IsRepeat ? vm.SelectedPage?.Widgets.Any(w => w.KeyShortcut == name) == true : vm.ShortcutDown(name);
        }
    }

    /// <summary>In the Shows list Space plays/pauses the selected show; elsewhere the show in the transport bar.</summary>
    private bool TogglePlayPause(ShowPlayerViewModel player)
    {
        if (ShowsPanel.IsKeyboardFocusWithin && player.SelectedShow is { } selected)
        {
            player.PlayPauseRowCommand.Execute(selected);
            return true;
        }

        if (player.PlayPauseCommand.CanExecute(null))
        {
            player.PlayPauseCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Vm is { } vm && ConsoleKeyNames.From(key) is { } name)
        {
            vm.ShortcutUp(name);
        }
    }

    // ───────────────────────────── Page tabs (inline rename) ─────────────────────────────

    private void OnPageTabsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { } vm && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(PageTabs, source) is ListBoxItem { DataContext: VirtualConsolePageViewModel page })
        {
            vm.RenamePageCommand.Execute(page);
            e.Handled = true;
        }
    }

    private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    box.Focus();
                    box.SelectAll();
                },
                DispatcherPriority.Input);
        }
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || sender is not TextBox { DataContext: VirtualConsolePageViewModel page })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.CommitRenameCommand.Execute(page);
            PageTabs.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRenameCommand.Execute(page);
            PageTabs.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Vm is { } vm && sender is TextBox { DataContext: VirtualConsolePageViewModel { IsRenaming: true } page })
        {
            vm.CommitRenameCommand.Execute(page);
        }
    }

    // ───────────────────────────── Canvas sizing ─────────────────────────────

    private void OnCanvasHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateCellSize();

    private void UpdateCellSize()
    {
        if (Vm is not { } vm || CanvasHost.ActualWidth <= 0)
        {
            return;
        }

        var columns = Math.Max(1, vm.SelectedPage?.Columns ?? 24);
        var rows = Math.Max(1, vm.SelectedPage?.Rows ?? 16);
        var fit = Math.Min(
            (CanvasHost.ActualWidth - (2 * SurfaceMargin)) / columns,
            (CanvasHost.ActualHeight - (2 * SurfaceMargin)) / rows);
        vm.CellSize = Math.Max(VirtualConsoleViewModel.MinCellSize, Math.Floor(fit));
    }

    // ───────────────────────────── Edit mode: select, move, resize ─────────────────────────────

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        CanvasHost.Focus();
        Vm?.SelectWidget(null);
    }

    private void OnEditLayerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: ConsoleWidgetViewModel widget } layer)
        {
            return;
        }

        vm.SelectWidget(widget);
        CanvasHost.Focus();
        _dragWidget = widget;
        _resizing = e.OriginalSource is FrameworkElement { Tag: "Resize" };
        _dragStart = e.GetPosition(WidgetSurface);
        (_dragStartA, _dragStartB) = _resizing ? (widget.Width, widget.Height) : (widget.X, widget.Y);
        layer.CaptureMouse();
        e.Handled = true;
    }

    private void OnEditLayerMouseMove(object sender, MouseEventArgs e)
    {
        if (Vm is not { } vm || _dragWidget is not { } widget || sender is not UIElement { IsMouseCaptured: true })
        {
            return;
        }

        var point = e.GetPosition(WidgetSurface);
        var dx = (point.X - _dragStart.X) / vm.CellSize;
        var dy = (point.Y - _dragStart.Y) / vm.CellSize;
        if (_resizing)
        {
            vm.ResizeWidget(widget, _dragStartA + dx, _dragStartB + dy);
        }
        else
        {
            vm.MoveWidget(widget, _dragStartA + dx, _dragStartB + dy);
        }
    }

    private void OnEditLayerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement { IsMouseCaptured: true } layer)
        {
            layer.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnEditLayerLostCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragWidget is not null)
        {
            _dragWidget = null;
            Vm?.EndLayoutChange();
        }
    }

    // ───────────────────────────── Palette drag-and-drop ─────────────────────────────

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e) => _paletteDragStart = e.GetPosition(this);

    private void OnPaletteMouseMove(object sender, MouseEventArgs e)
    {
        if (_paletteDragStart is not { } start || e.LeftButton != MouseButtonState.Pressed
            || sender is not FrameworkElement { DataContext: VirtualConsolePaletteItem item } source)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _paletteDragStart = null;
        DragDrop.DoDragDrop(source, new DataObject(PaletteDragFormat, item.Kind), DragDropEffects.Copy);
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Vm is { IsEditMode: true } && e.Data.GetDataPresent(PaletteDragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        if (Vm is not { IsEditMode: true } vm || e.Data.GetData(PaletteDragFormat) is not VirtualConsoleWidgetKind kind)
        {
            return;
        }

        var point = e.GetPosition(WidgetSurface);
        vm.AddWidgetAt(kind, (int)Math.Floor(point.X / vm.CellSize), (int)Math.Floor(point.Y / vm.CellSize));
        CanvasHost.Focus();
        e.Handled = true;
    }
}
