using System.Windows.Controls;

namespace DmxPro.App.Views.Editors;

/// <summary>Flash editor. DataContext: <c>FlashEditorViewModel</c>.</summary>
public partial class FlashEditorView : UserControl
{
    public FlashEditorView()
    {
        InitializeComponent();
        EffectEditorPicker.Attach(this);
    }
}
