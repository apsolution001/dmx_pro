using System.Collections;
using System.Windows.Controls;

namespace DmxPro.App.Controls;

/// <summary>
/// A <see cref="DataGrid"/> whose multi-selection can be replaced in one go. Adding hundreds of rows to
/// <see cref="System.Windows.Controls.Primitives.MultiSelector.SelectedItems"/> one by one raises a selection change
/// per row; this batches them into one (used by the "Choose lights" dialog when the ViewModel changes the picked set).
/// </summary>
public sealed class BulkSelectDataGrid : DataGrid
{
    /// <summary>Makes <paramref name="items"/> the whole selection, raising one <c>SelectionChanged</c>.</summary>
    public void SelectOnly(IEnumerable items)
    {
        ArgumentNullException.ThrowIfNull(items);
        BeginUpdateSelectedItems();
        try
        {
            SelectedItems.Clear();
            foreach (var item in items)
            {
                SelectedItems.Add(item);
            }
        }
        finally
        {
            EndUpdateSelectedItems();
        }
    }
}
