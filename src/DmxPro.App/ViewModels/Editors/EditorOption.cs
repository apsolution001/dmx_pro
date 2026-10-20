namespace DmxPro.App.ViewModels.Editors;

/// <summary>A choice in an editor combo box (run order, direction, shape, mode …): the value and its label.</summary>
/// <typeparam name="T">The value type, usually an enum.</typeparam>
public sealed record EditorOption<T>(T Value, string Label, string ToolTip)
{
    public override string ToString() => Label;
}
