using System.Windows.Controls;

namespace DmxPro.App.Views;

/// <summary>
/// The function editor host (toolbar, banners, then the editor of the function's type), shared by the Functions and
/// Shows pages. DataContext: a <c>FunctionEditorViewModel</c>.
/// </summary>
public partial class FunctionEditorPanel : UserControl
{
    public FunctionEditorPanel()
    {
        InitializeComponent();
    }
}
