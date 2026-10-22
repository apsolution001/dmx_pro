using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App.Views;

/// <summary>
/// Modal "Choose lights" dialog. Code-behind only keeps the grid's multi-selection and the ViewModel's picked set in
/// step (<c>DataGrid.SelectedItems</c> cannot be bound) and handles focus and keys; the rules live in
/// <see cref="FixturePickerViewModel"/>.
/// </summary>
public partial class FixturePickerWindow : FluentWindow
{
    private readonly FixturePickerViewModel _viewModel;
    private bool _syncing;

    public FixturePickerWindow(FixturePickerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _viewModel.SelectionReset += OnSelectionReset;
        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.SelectionReset -= OnSelectionReset;
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>; true when the user clicked OK.</summary>
    public static bool ShowModal(FixturePickerViewModel viewModel, Window? owner)
    {
        var window = new FixturePickerWindow(viewModel) { Owner = owner };
        return window.ShowDialog() == true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncGridSelection();

        // Start in the list on the first used light, so arrows, Shift+arrows and Ctrl+A work at once.
        LightsGrid.Focus();
        var first = LightsGrid.SelectedItems.Count > 0 ? LightsGrid.SelectedItems[0] : null;
        if (first is null && LightsGrid.Items.Count > 0)
        {
            first = LightsGrid.Items[0];
        }

        if (first is not null)
        {
            LightsGrid.ScrollIntoView(first);
            LightsGrid.CurrentCell = new DataGridCellInfo(first, LightsGrid.Columns[0]);
        }
    }

    private void OnSelectionReset(object? sender, EventArgs e) => SyncGridSelection();

    /// <summary>The grid is re-attached when the Lights tab is shown again: make sure it shows the current picks.</summary>
    private void OnLightsGridLoaded(object sender, RoutedEventArgs e) => SyncGridSelection();

    /// <summary>Copies the ViewModel's picked lights into the grid selection without reporting it back.</summary>
    private void SyncGridSelection()
    {
        _syncing = true;
        try
        {
            var picked = new List<FixturePickerLight>();
            foreach (var light in _viewModel.VisibleLights)
            {
                if (light.IsPicked)
                {
                    picked.Add(light);
                }
            }

            LightsGrid.SelectOnly(picked);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnLightsGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ReferenceEquals(e.OriginalSource, LightsGrid))
        {
            _viewModel.ApplyListSelection(e.AddedItems, e.RemovedItems);
        }
    }

    /// <summary>The grid uses Enter to move down a row; in this dialog Enter means OK, as everywhere else.</summary>
    private void OnLightsGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            DialogResult = true;
        }
    }

    /// <summary>
    /// Enter in the search box ends the search instead of pressing the default OK button: focus moves to the first
    /// matching light, where Enter means OK.
    /// </summary>
    private void OnSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        LightsGrid.Focus();
        if (LightsGrid.Items.Count > 0 && LightsGrid.Columns.Count > 0)
        {
            var first = LightsGrid.Items[0];
            LightsGrid.ScrollIntoView(first);
            LightsGrid.CurrentCell = new DataGridCellInfo(first, LightsGrid.Columns[0]);
        }
    }

    /// <summary>Ctrl+F: go to the search box on the Lights tab.</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            LightsTab.IsSelected = true;
            Dispatcher.BeginInvoke(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            });
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
