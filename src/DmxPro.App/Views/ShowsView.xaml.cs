using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Shows page. Code-behind holds only pointer plumbing: it converts mouse positions to seconds / tracks and calls
/// <see cref="ShowsViewModel"/> (drag to move or resize items, drag on empty lane to select a range, library
/// drag-and-drop, ruler clicks and scrubbing, dragging the song on the audio lane, dropping music files, fast
/// rewind / forward repeat timing, following the playhead, scroll syncing between ruler, headers and lanes,
/// Ctrl+wheel zoom).
/// </summary>
public partial class ShowsView : UserControl
{
    /// <summary>Width of the grab zone at each end of an item block for resizing.</summary>
    private const double EdgeGrip = 6;

    private const double ZoomStep = 1.2;

    /// <summary>Drag-and-drop format carrying a library function id (<see cref="Guid"/>).</summary>
    private const string FunctionDragFormat = "DmxPro.ShowsLibraryFunction";

    private DragKind _drag;
    private bool _dragMoved;
    private Point _dragOrigin;
    private ShowItemViewModel? _dragItem;
    private ShowTrackViewModel? _dragTrack;
    private double _originalStart;
    private double _originalLength;
    private double _pendingStart;
    private double _pendingLength;
    private double _rangeAnchor;

    /// <summary>A held rewind / forward button repeats faster than this (ms); a slower click starts over at one step.</summary>
    private const long SkipRepeatWindowMs = 400;

    /// <summary>When following, the playhead is put this far (fraction of the view) from the left edge after a jump.</summary>
    private const double FollowLeadFraction = 0.1;

    private Point _libraryDownPoint;
    private ShowsLibraryItem? _libraryDownItem;

    private double _scrubSeconds;

    private bool _audioDragging;
    private bool _audioDragMoved;
    private double _audioDragOriginX;
    private double _audioDragOriginOffset;
    private double _audioDragPending;

    private long _lastSkipTick;
    private int _skipRepeats;

    private ShowsViewModel? _hooked;

    /// <summary>The last right-click on the lanes hit a block (the block menu may open).</summary>
    private bool _rightClickOnItem;

    public ShowsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Hook(ViewModel);
        Unloaded += (_, _) => Hook(null);
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                Hook(ViewModel);
            }
        };
    }

    private enum DragKind
    {
        None,
        Move,
        ResizeStart,
        ResizeEnd,
        Range,
    }

    private ShowsViewModel? ViewModel => DataContext as ShowsViewModel;

    // ───────────── Lanes: select, move, resize, range ─────────────

    private void OnLaneMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        LaneScroll.Focus();
        var p = e.GetPosition(LaneHost);
        _dragOrigin = p;
        _dragMoved = false;

        if (FindData<ShowItemViewModel>(e.OriginalSource as DependencyObject) is { } item)
        {
            vm.SelectItem(item);

            // Double-click on a block opens its editor (no drag).
            if (e.ClickCount == 2)
            {
                if (vm.EditSelectedItemCommand.CanExecute(null))
                {
                    vm.EditSelectedItemCommand.Execute(null);
                }

                _drag = DragKind.None;
                e.Handled = true;
                return;
            }

            var localX = p.X - item.Left;
            var resizable = item.Width >= EdgeGrip * 3;
            _drag = resizable && localX <= EdgeGrip ? DragKind.ResizeStart
                : resizable && localX >= item.Width - EdgeGrip ? DragKind.ResizeEnd
                : DragKind.Move;
            _dragItem = item;
            _dragTrack = item.Track;
            _originalStart = _pendingStart = item.Item.StartSeconds;
            _originalLength = _pendingLength = item.Item.LengthSeconds;
        }
        else
        {
            vm.ClearSelection();
            var track = TrackAt(p.Y);
            vm.SetActiveTrack(track);
            if (track is null)
            {
                return;
            }

            _drag = DragKind.Range;
            _dragTrack = track;
            _rangeAnchor = vm.Snap(p.X / vm.PixelsPerSecond);
        }

        LaneHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnLaneMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == DragKind.None || ViewModel is not { } vm)
        {
            return;
        }

        var p = e.GetPosition(LaneHost);
        if (!_dragMoved
            && Math.Abs(p.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragMoved = true;
        var pps = vm.PixelsPerSecond;
        var delta = (p.X - _dragOrigin.X) / pps;
        var originalEnd = _originalStart + _originalLength;
        switch (_drag)
        {
            case DragKind.Move:
                _pendingStart = vm.Snap(_originalStart + delta);
                _pendingLength = _originalLength;
                _dragTrack = TrackAt(p.Y) ?? _dragTrack;
                vm.SetDropTarget(ReferenceEquals(_dragTrack, _dragItem!.Track) ? null : _dragTrack);
                break;
            case DragKind.ResizeStart:
                _pendingStart = Math.Max(0, Math.Min(vm.Snap(_originalStart + delta), originalEnd - ShowsViewModel.MinItemSeconds));
                _pendingLength = originalEnd - _pendingStart;
                break;
            case DragKind.ResizeEnd:
                _pendingStart = _originalStart;
                _pendingLength = Math.Max(vm.Snap(originalEnd + delta), _originalStart + ShowsViewModel.MinItemSeconds) - _originalStart;
                break;
            case DragKind.Range:
                vm.SetRange(_dragTrack!, _rangeAnchor, vm.Snap(Math.Max(0, p.X) / pps));
                return;
        }

        _dragItem!.Layout(_pendingStart, _pendingLength, pps);
    }

    private void OnLaneMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var kind = _drag;
        _drag = DragKind.None;
        LaneHost.ReleaseMouseCapture();
        if (kind is DragKind.Move or DragKind.ResizeStart or DragKind.ResizeEnd && _dragMoved && ViewModel is { } vm)
        {
            vm.CommitItemMove(_dragItem!, _dragTrack ?? _dragItem!.Track, _pendingStart, _pendingLength);
        }

        _dragItem = null;
        _dragTrack = null;
    }

    /// <summary>Capture lost mid-drag (Alt+Tab, Esc dialogs…): put the block back where it was.</summary>
    private void OnLaneLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_drag == DragKind.None)
        {
            return;
        }

        if (_dragItem is not null && ViewModel is { } vm)
        {
            _dragItem.Layout(_originalStart, _originalLength, vm.PixelsPerSecond);
            vm.SetDropTarget(null);
        }

        _drag = DragKind.None;
        _dragItem = null;
        _dragTrack = null;
    }

    // ───────────── Block menu (right-click a block, or Shift+F10 / menu key for the selected block) ─────────────

    /// <summary>Right-click on a block selects it, so the menu acts on it; elsewhere on the lanes no menu opens.</summary>
    private void OnLaneMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        LaneScroll.Focus();
        var item = FindData<ShowItemViewModel>(e.OriginalSource as DependencyObject);
        _rightClickOnItem = item is not null;
        if (item is not null)
        {
            vm.SelectItem(item);
        }
    }

    private void OnLaneContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (LaneScroll.ContextMenu is not { } menu)
        {
            return;
        }

        // Keyboard (Shift+F10 / menu key) reports -1, -1.
        var fromKeyboard = e.CursorLeft < 0 && e.CursorTop < 0;
        var item = ViewModel?.SelectedItem;
        var onItem = fromKeyboard ? item is not null : _rightClickOnItem && item is not null;
        _rightClickOnItem = false;
        if (!onItem)
        {
            e.Handled = true;
            return;
        }

        if (!fromKeyboard)
        {
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            menu.Placement = PlacementMode.MousePoint;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            return;
        }

        // By keyboard: open it under the selected block instead of at the corner of the timeline.
        e.Handled = true;
        var row = ViewModel!.Tracks.IndexOf(item!.Track);
        menu.PlacementTarget = LaneHost;
        menu.Placement = PlacementMode.Relative;
        menu.HorizontalOffset = item.Left;
        menu.VerticalOffset = (Math.Max(0, row) * ShowsViewModel.TrackHeight) + ShowsViewModel.ItemTop + ShowsViewModel.ItemHeight;
        menu.IsOpen = true;
    }

    private void OnLanePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zoom around the pointer: keep the time under the mouse in place.
            var x = e.GetPosition(LaneScroll).X;
            var time = (LaneScroll.HorizontalOffset + x) / vm.PixelsPerSecond;
            vm.PixelsPerSecond = Math.Clamp(
                e.Delta > 0 ? vm.PixelsPerSecond * ZoomStep : vm.PixelsPerSecond / ZoomStep,
                ShowsViewModel.MinZoom,
                ShowsViewModel.MaxZoom);
            LaneScroll.UpdateLayout();
            LaneScroll.ScrollToHorizontalOffset(Math.Max(0, (time * vm.PixelsPerSecond) - x));
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            LaneScroll.ScrollToHorizontalOffset(LaneScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    // ───────────── Ruler: move the playhead (one seek on release, not one per mouse move) ─────────────

    private void OnRulerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (!vm.CanControlTransport)
        {
            // Live mode: the ruler does not seek (the playhead only follows the Virtual Console).
            LaneScroll.Focus();
            e.Handled = true;
            return;
        }

        vm.BeginScrub();
        _scrubSeconds = RulerSeconds(vm, e);
        vm.ScrubTo(_scrubSeconds);
        RulerArea.CaptureMouse();
        LaneScroll.Focus();
        e.Handled = true;
    }

    private void OnRulerMouseMove(object sender, MouseEventArgs e)
    {
        if (RulerArea.IsMouseCaptured && ViewModel is { } vm)
        {
            _scrubSeconds = RulerSeconds(vm, e);
            vm.ScrubTo(_scrubSeconds);
        }
    }

    private void OnRulerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && RulerArea.IsMouseCaptured)
        {
            vm.EndScrub(RulerSeconds(vm, e));
        }

        RulerArea.ReleaseMouseCapture();
    }

    /// <summary>Capture lost mid-drag (Alt+Tab…): seek to the last position (no-op when already released).</summary>
    private void OnRulerLostMouseCapture(object sender, MouseEventArgs e) => ViewModel?.EndScrub(_scrubSeconds);

    private double RulerSeconds(ShowsViewModel vm, MouseEventArgs e) =>
        vm.Snap(Math.Max(0, e.GetPosition(RulerArea).X) / vm.PixelsPerSecond);

    // ───────────── Transport: fast rewind / forward, following the playhead ─────────────

    private void OnRewindClick(object sender, RoutedEventArgs e) => Skip(-1);

    private void OnForwardClick(object sender, RoutedEventArgs e) => Skip(1);

    /// <summary>A RepeatButton click: the first press jumps one step; a held button's repeats count up and speed up.</summary>
    private void Skip(int direction)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var now = Environment.TickCount64;
        _skipRepeats = now - _lastSkipTick <= SkipRepeatWindowMs ? _skipRepeats + 1 : 0;
        _lastSkipTick = now;
        vm.SkipBy(direction, _skipRepeats);
        EnsurePlayheadVisible(vm);
    }

    private void Hook(ShowsViewModel? vm)
    {
        if (ReferenceEquals(vm, _hooked))
        {
            return;
        }

        if (_hooked is not null)
        {
            _hooked.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _hooked = vm;
        if (vm is not null)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShowsViewModel.IsLive)
            && sender is ShowsViewModel { IsLive: true }
            && RulerArea.IsMouseCaptured)
        {
            // Live mode turned on mid-drag: end it without a seek (the ViewModel already cancelled the scrub).
            RulerArea.ReleaseMouseCapture();
            return;
        }

        if (e.PropertyName == nameof(ShowsViewModel.PlayheadSeconds)
            && sender is ShowsViewModel { FollowPlayhead: true, IsShowRunning: true } vm
            && !RulerArea.IsMouseCaptured)
        {
            EnsurePlayheadVisible(vm);
        }
    }

    /// <summary>Scrolls by a page when the playhead left the view (not every frame: the timeline stays still while it moves).</summary>
    private void EnsurePlayheadVisible(ShowsViewModel vm)
    {
        var viewport = LaneScroll.ViewportWidth;
        if (viewport <= 0)
        {
            return;
        }

        var x = vm.PlayheadX;
        var offset = LaneScroll.HorizontalOffset;
        if (x < offset || x > offset + viewport - 16)
        {
            LaneScroll.ScrollToHorizontalOffset(Math.Max(0, x - (viewport * FollowLeadFraction)));
        }
    }

    // ───────────── Audio lane: drag the song in time, drop music files, music menu ─────────────

    private void OnAudioLaneMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        LaneScroll.Focus();
        var music = vm.Music;
        if (e.ClickCount == 2 && music.HasNoMusic)
        {
            if (music.AddMusicCommand.CanExecute(null))
            {
                music.AddMusicCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        // Nothing selected: Properties shows the show and its music settings.
        vm.ClearSelection();
        var x = e.GetPosition(AudioLane).X;
        if (IsOverSong(vm, x))
        {
            _audioDragging = true;
            _audioDragMoved = false;
            _audioDragOriginX = x;
            _audioDragOriginOffset = _audioDragPending = music.LaneOffsetSeconds;
            AudioLane.CaptureMouse();
        }

        e.Handled = true;
    }

    private void OnAudioLaneMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var x = e.GetPosition(AudioLane).X;
        if (!_audioDragging)
        {
            AudioLane.Cursor = IsOverSong(vm, x) ? Cursors.SizeWE : null;
            return;
        }

        if (!_audioDragMoved && Math.Abs(x - _audioDragOriginX) < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        _audioDragMoved = true;
        var raw = _audioDragOriginOffset + ((x - _audioDragOriginX) / vm.PixelsPerSecond);
        _audioDragPending = vm.Music.SnapOffset(raw);
        vm.Music.PreviewOffset(_audioDragPending);
    }

    private void OnAudioLaneMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var commit = _audioDragging && _audioDragMoved;
        _audioDragging = false;
        AudioLane.ReleaseMouseCapture();
        if (commit && ViewModel is { } vm)
        {
            vm.Music.CommitOffset(_audioDragPending);
        }
    }

    /// <summary>Capture lost mid-drag (Alt+Tab, a dialog…): put the song back where it was.</summary>
    private void OnAudioLaneLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_audioDragging)
        {
            return;
        }

        _audioDragging = false;
        ViewModel?.Music.CancelOffsetPreview();
    }

    private static bool IsOverSong(ShowsViewModel vm, double x)
    {
        var music = vm.Music;
        if (!music.HasMusic || music.LaneDurationSeconds <= 0)
        {
            return false;
        }

        var start = music.LaneOffsetSeconds * vm.PixelsPerSecond;
        var end = (music.LaneOffsetSeconds + music.LaneDurationSeconds) * vm.PixelsPerSecond;
        return x >= start && x <= end;
    }

    private void OnAudioLaneDragOver(object sender, DragEventArgs e)
    {
        var ok = ViewModel is { HasShow: true } && DroppedMusicFile(e) is not null;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        if (ok)
        {
            AudioLane.SetResourceReference(BackgroundProperty, "SubtleFillColorSecondaryBrush");
        }

        e.Handled = true;
    }

    private void OnAudioLaneDragLeave(object sender, DragEventArgs e) => AudioLane.Background = Brushes.Transparent;

    private void OnAudioLaneDrop(object sender, DragEventArgs e)
    {
        AudioLane.Background = Brushes.Transparent;
        if (ViewModel is { HasShow: true } vm && DroppedMusicFile(e) is { } path)
        {
            _ = vm.Music.DropFileAsync(path);
        }

        e.Handled = true;
    }

    /// <summary>The one music file being dragged (from Explorer), or null (none, several, or not a music file).</summary>
    private static string? DroppedMusicFile(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
        && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && ShowAudioViewModel.IsSupportedFile(files[0])
            ? files[0]
            : null;

    /// <summary>The "⋯" button opens the music actions menu (also by keyboard: Enter / Space).</summary>
    private void OnMusicMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (MusicMenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MusicMenuButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    // ───────────── Scroll syncing ─────────────

    private void OnLaneScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RulerScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        HeaderScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    /// <summary>The header column follows the lanes; a wheel over it scrolls the lanes instead.</summary>
    private void OnHeaderPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        LaneScroll.ScrollToVerticalOffset(LaneScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // ───────────── Library drag-and-drop ─────────────

    private void OnLibraryPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _libraryDownPoint = e.GetPosition(LibraryList);
        _libraryDownItem = FindData<ShowsLibraryItem>(e.OriginalSource as DependencyObject);
    }

    private void OnLibraryPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_libraryDownItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _libraryDownItem = null;
            return;
        }

        var p = e.GetPosition(LibraryList);
        if (Math.Abs(p.X - _libraryDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _libraryDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _libraryDownItem;
        _libraryDownItem = null;
        DragDrop.DoDragDrop(LibraryList, new DataObject(FunctionDragFormat, item.Id), DragDropEffects.Copy);
        ViewModel?.SetDropTarget(null);
    }

    private void OnLaneDragOver(object sender, DragEventArgs e)
    {
        var track = e.Data.GetDataPresent(FunctionDragFormat) ? TrackAt(e.GetPosition(LaneHost).Y) : null;
        e.Effects = track is null ? DragDropEffects.None : DragDropEffects.Copy;
        ViewModel?.SetDropTarget(track);
        e.Handled = true;
    }

    private void OnLaneDragLeave(object sender, DragEventArgs e) => ViewModel?.SetDropTarget(null);

    private void OnLaneDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || e.Data.GetData(FunctionDragFormat) is not Guid functionId)
        {
            return;
        }

        var p = e.GetPosition(LaneHost);
        vm.SetDropTarget(null);
        if (TrackAt(p.Y) is { } track)
        {
            vm.PlaceFunction(functionId, track, vm.Snap(Math.Max(0, p.X) / vm.PixelsPerSecond));
            LaneScroll.Focus();
        }

        e.Handled = true;
    }

    // ───────────── Inline rename boxes ─────────────

    /// <summary>The inline rename box takes focus and selects its text when it appears.</summary>
    private void OnRenameBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    /// <summary>Clicking elsewhere commits the rename, like Explorer.</summary>
    private void OnShowRenameBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel is { IsRenamingShow: true } vm)
        {
            vm.CommitRenameShowCommand.Execute(null);
        }
    }

    private void OnTrackRenameBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ShowTrackViewModel { IsEditing: true } track })
        {
            track.CommitRenameCommand.Execute(null);
        }
    }

    // ───────────── Track headers: focus (for Shift+F10 / F2) and activation ─────────────

    /// <summary>
    /// A click on a header focuses it (so Shift+F10, the menu key and F2 work on it); a right-click also makes it the
    /// active track. Clicks on its buttons and the rename box keep their own focus handling.
    /// </summary>
    private void OnTrackHeaderPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShowTrackViewModel track } header
            || IsInside<TextBoxBase>(e.OriginalSource as DependencyObject, header)
            || IsInside<ButtonBase>(e.OriginalSource as DependencyObject, header))
        {
            return;
        }

        header.Focus();
        if (e.ChangedButton == MouseButton.Right)
        {
            track.ActivateCommand.Execute(null);
        }
    }

    /// <summary>Tabbing onto a header makes it the active track (where Place puts functions).</summary>
    private void OnTrackHeaderGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, sender) && sender is FrameworkElement { DataContext: ShowTrackViewModel track })
        {
            track.ActivateCommand.Execute(null);
        }
    }

    /// <summary>True when <paramref name="element"/> is (inside) a <typeparamref name="T"/> below <paramref name="root"/>.</summary>
    private static bool IsInside<T>(DependencyObject? element, DependencyObject root)
        where T : DependencyObject
    {
        while (element is not null && !ReferenceEquals(element, root))
        {
            if (element is T)
            {
                return true;
            }

            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    // ───────────── Helpers ─────────────

    /// <summary>The track lane at a y position in <see cref="LaneHost"/>, or null below the last track.</summary>
    private ShowTrackViewModel? TrackAt(double y)
    {
        if (ViewModel is not { } vm || y < 0)
        {
            return null;
        }

        var index = (int)(y / ShowsViewModel.TrackHeight);
        return index < vm.Tracks.Count ? vm.Tracks[index] : null;
    }

    /// <summary>Walks up from a hit element to the nearest element whose DataContext is a <typeparamref name="T"/>.</summary>
    private T? FindData<T>(DependencyObject? element)
        where T : class
    {
        while (element is not null && !ReferenceEquals(element, this))
        {
            if (element is FrameworkElement { DataContext: T data })
            {
                return data;
            }

            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }
}
