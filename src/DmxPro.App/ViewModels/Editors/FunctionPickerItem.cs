using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>A function offered by the chaser editor's "Add step" picker.</summary>
public sealed record FunctionPickerItem(LightFunction Function)
{
    public string Name => Function.Name;

    public string TypeName => FunctionRowViewModel.TypeDisplayName(Function.Type);

    public string IconKind => FunctionRowViewModel.TypeIconKind(Function.Type);

    public string AutomationName => $"{Name}, {TypeName}";
}
