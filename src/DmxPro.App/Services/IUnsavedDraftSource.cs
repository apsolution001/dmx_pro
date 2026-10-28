namespace DmxPro.App.Services;

/// <summary>
/// A page with an editor whose draft is not in the project yet (Functions, Shows). An external change of the project
/// file counts such a draft as unsaved work and asks before reloading.
/// </summary>
public interface IUnsavedDraftSource
{
    /// <summary>True while an open editor has changes that were not applied to the project.</summary>
    bool HasUnsavedDraft { get; }
}
