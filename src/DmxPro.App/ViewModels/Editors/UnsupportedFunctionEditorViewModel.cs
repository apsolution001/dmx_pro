using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Shown for a function type DMX Pro no longer edits (an old colour cycle, fade, pulse, flash or strobe that was not
/// converted when the project loaded): a short note instead of an editor, so opening it never crashes. Only the name
/// can be changed; the function's settings are left exactly as they are.
/// </summary>
public sealed class UnsupportedFunctionEditorViewModel : FunctionEditorViewModel
{
    public UnsupportedFunctionEditorViewModel(Project project, LightFunction original)
        : base(project, original)
    {
        LoadFromOriginal();
    }

    /// <summary>The note shown in place of an editor.</summary>
    public string Message => "This function type is no longer supported. DMX Pro now offers scenes and chasers only.";

    protected override void ApplyDraft()
    {
    }

    protected override void LoadDraft()
    {
    }
}
