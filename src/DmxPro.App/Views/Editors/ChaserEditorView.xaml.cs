using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DmxPro.App.ViewModels.Editors;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Chaser editor. Code-behind only handles view concerns: focus (picker flyout, row selection when a timing box is
/// focused), mapping keys / double-clicks to ViewModel commands, and the drag-to-move gesture with its insertion line.
/// All list changes are ViewModel commands. DataContext: <c>ChaserEditorViewModel</c>.
/// </summary>
public partial class ChaserEditorView : UserControl
{
    private const string DragFormat = "DmxPro.ChaserSteps";

    private Point? _dragStart;
    private bool _focusStepsOnClose;
    private InsertionLineAdorner? _insertionLine;

    public ChaserEditorView()
    {
        InitializeComponent();
    }

    private ChaserEditorViewModel? ViewModel => DataContext as ChaserEditorViewModel;

    /// <summary>Typing in a step's timing box selects that step (only it, unless it is already part of the selection).</summary>
    private void OnRowGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Only for the timing boxes: a click on a row is the list's own selection (Ctrl / Shift + click included).
        if (sender is DataGridRow { IsSelected: false } row && e.NewFocus is System.Windows.Controls.Primitives.TextBoxBase)
        {
            StepGrid.SelectedItem = row.Item;
        }
    }

    /// <summary>Ctrl+Up / Ctrl+Down (and Alt+arrows) move the selection, also while a timing box has focus.</summary>
    private void OnStepGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if ((key != Key.Up && key != Key.Down) || (modifiers != ModifierKeys.Control && modifiers != ModifierKeys.Alt)
            || ViewModel is not { } vm)
        {
            return;
        }

        var command = key == Key.Up ? vm.MoveStepsUpCommand : vm.MoveStepsDownCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    // ───────────── Add-steps picker ─────────────

    /// <summary>A popup does not take focus by itself; put it in the search box.</summary>
    private void OnAddPopupOpened(object? sender, EventArgs e)
    {
        _focusStepsOnClose = false;
        PickerSearchBox.Dispatcher.BeginInvoke(() => PickerSearchBox.Focus());
    }

    /// <summary>
    /// After adding, focus goes to the first new step, so Del / Ctrl+arrows act on it at once. A click elsewhere that
    /// closes the flyout keeps the focus where it was clicked.
    /// </summary>
    private void OnAddPopupClosed(object? sender, EventArgs e)
    {
        if (!_focusStepsOnClose)
        {
            return;
        }

        _focusStepsOnClose = false;
        StepGrid.Dispatcher.BeginInvoke(() =>
        {
            if (StepGrid.SelectedItem is { } item && StepGrid.Columns.Count > 0)
            {
                StepGrid.ScrollIntoView(item);
                StepGrid.CurrentCell = new DataGridCellInfo(item, StepGrid.Columns[0]);
                if (StepGrid.Columns[0].GetCellContent(item)?.Parent is DataGridCell cell)
                {
                    cell.Focus();
                    return;
                }
            }

            StepGrid.Focus();
        });
    }

    /// <summary>Enter anywhere in the flyout adds; remember to move focus to the steps once it closes.</summary>
    private void OnAddPopupPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _focusStepsOnClose = true;
        }
    }

    private void OnPickerAddClick(object sender, RoutedEventArgs e) => _focusStepsOnClose = true;

    /// <summary>Esc closes the flyout and returns focus to the Add button.</summary>
    private void OnAddPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { } vm)
        {
            vm.IsPickerOpen = false;
            AddToggle.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Down arrow in the search box moves into the list; Enter adds the first match.</summary>
    private void OnPickerSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && PickerList.Items.Count > 0)
        {
            if (PickerList.SelectedIndex < 0)
            {
                PickerList.SelectedIndex = 0;
            }

            if (PickerList.ItemContainerGenerator.ContainerFromIndex(PickerList.SelectedIndex) is UIElement item)
            {
                item.Focus();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ViewModel?.AddFirstMatchCommand is { } add)
        {
            add.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Double-click on a function adds it as a step.</summary>
    private void OnPickerItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is ListBoxItem { DataContext: ChaserPickerItem item }
            && ViewModel?.AddStepsCommand is { } add && add.CanExecute(item))
        {
            _focusStepsOnClose = true;
            add.Execute(item);
            e.Handled = true;
        }
    }

    // ───────────── Drag to move (grip in the # column) ─────────────

    /// <summary>
    /// Pressing the grip picks the step up: a step outside the selection becomes the only selection, a selected one
    /// takes the whole selection along. Handled, so the list does not start a drag-selection instead. With Ctrl or
    /// Shift the click is left to the list (selecting several).
    /// </summary>
    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChaserStepRowViewModel row } || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (!row.IsSelected)
        {
            StepGrid.SelectedItem = row;
        }

        StepGrid.Focus();
        _dragStart = e.GetPosition(StepGrid);
        e.Handled = true;
    }

    private void OnGripMouseUp(object sender, MouseButtonEventArgs e) => _dragStart = null;

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            return;
        }

        var delta = e.GetPosition(StepGrid) - start;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance
            && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        _dragStart = null;
        DragDrop.DoDragDrop(StepGrid, new DataObject(DragFormat, true), DragDropEffects.Move);
        RemoveInsertionLine();
    }

    private void OnStepGridDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        ShowInsertionLine(InsertionPoint(e));
        e.Handled = true;
    }

    private void OnStepGridDragLeave(object sender, DragEventArgs e) => RemoveInsertionLine();

    private void OnStepGridDrop(object sender, DragEventArgs e)
    {
        RemoveInsertionLine();
        if (!e.Data.GetDataPresent(DragFormat) || ViewModel?.MoveStepsToCommand is not { } move)
        {
            return;
        }

        var (index, _) = InsertionPoint(e);
        if (move.CanExecute(index))
        {
            move.Execute(index);
        }

        e.Handled = true;
    }

    /// <summary>The gap the drop goes into (0 = above the first row) and the line's Y position in the grid.</summary>
    private (int Index, double Y) InsertionPoint(DragEventArgs e)
    {
        var position = e.GetPosition(StepGrid);
        var row = FindRow(StepGrid.InputHitTest(position) as DependencyObject);
        if (row is null)
        {
            // Below the last row (or on the header): the end of the list.
            var last = StepGrid.Items.Count == 0 ? null : StepGrid.ItemContainerGenerator.ContainerFromIndex(StepGrid.Items.Count - 1) as DataGridRow;
            var y = last is null ? 0 : last.TranslatePoint(new Point(0, last.ActualHeight), StepGrid).Y;
            return position.Y < y && StepGrid.Items.Count > 0 ? (0, ColumnHeaderBottom()) : (StepGrid.Items.Count, y);
        }

        var index = StepGrid.ItemContainerGenerator.IndexFromContainer(row);
        var top = row.TranslatePoint(default, StepGrid).Y;
        var lower = e.GetPosition(row).Y > row.ActualHeight / 2;
        return lower ? (index + 1, top + row.ActualHeight) : (index, top);
    }

    private double ColumnHeaderBottom() =>
        StepGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow first ? first.TranslatePoint(default, StepGrid).Y : 0;

    private static DataGridRow? FindRow(DependencyObject? element)
    {
        while (element is not null and not DataGridRow)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as DataGridRow;
    }

    private void ShowInsertionLine((int Index, double Y) point)
    {
        if (_insertionLine is null && AdornerLayer.GetAdornerLayer(StepGrid) is { } layer)
        {
            _insertionLine = new InsertionLineAdorner(StepGrid);
            layer.Add(_insertionLine);
        }

        _insertionLine?.MoveTo(point.Y);
    }

    private void RemoveInsertionLine()
    {
        if (_insertionLine is not null)
        {
            AdornerLayer.GetAdornerLayer(StepGrid)?.Remove(_insertionLine);
            _insertionLine = null;
        }
    }

    /// <summary>The accent line that shows where dragged steps will land.</summary>
    private sealed class InsertionLineAdorner : Adorner
    {
        private double _y;

        public InsertionLineAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void MoveTo(double y)
        {
            if (_y != y)
            {
                _y = y;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var brush = (AdornedElement as FrameworkElement)?.TryFindResource("AccentFillColorDefaultBrush") as Brush ?? SystemColors.HighlightBrush;
            var width = AdornedElement.RenderSize.Width;
            drawingContext.DrawRectangle(brush, null, new Rect(0, Math.Max(0, _y - 1), width, 2));
        }
    }
}
