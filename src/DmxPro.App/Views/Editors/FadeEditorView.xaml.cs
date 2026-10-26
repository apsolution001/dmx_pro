using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DmxPro.App.Views.Editors;

/// <summary>Fade editor. DataContext: <c>FadeEditorViewModel</c>. Code-behind holds keyboard focus moves only.</summary>
public partial class FadeEditorView : UserControl
{
    public FadeEditorView()
    {
        InitializeComponent();
    }

    /// <summary>Down arrow in a scene search box moves into that panel's scene list (chosen row, else the first).</summary>
    private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || sender is not DependencyObject box || FindList(box) is not { } list || list.Items.Count == 0)
        {
            return;
        }

        var item = list.SelectedItem ?? list.Items[0];
        list.ScrollIntoView(item);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            list.Focus();
        }

        e.Handled = true;
    }

    // The search box and its list sit in the same DockPanel of the side panel template.
    private static ListBox? FindList(DependencyObject start)
    {
        var node = start;
        while (node is not null and not DockPanel)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return node is null ? null : FindChild<ListBox>(node);
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindChild<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
