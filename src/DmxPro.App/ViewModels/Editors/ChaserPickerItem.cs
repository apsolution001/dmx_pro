using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>A function offered by the chaser editor's "Add steps" picker, with its colour swatch (on screen only).</summary>
public sealed record ChaserPickerItem(LightFunction Function, string? ColorHex)
{
    public string Name => Function.Name;

    public string TypeName => FunctionRowViewModel.TypeDisplayName(Function.Type);

    public string IconKind => FunctionRowViewModel.TypeIconKind(Function.Type);

    public bool HasColor => ColorHex is not null;

    public string AutomationName => $"{Name}, {TypeName}";
}
