namespace DmxPro.App.Services;

/// <summary>What one auto-save attempt did (<see cref="ProjectFileSyncService.AutoSaveAsync"/>).</summary>
public enum AutoSaveOutcome
{
    /// <summary>Nothing to do: auto-save is off, the project has no file yet or no unsaved changes.</summary>
    NotNeeded,

    /// <summary>Saved through the normal save path.</summary>
    Saved,

    /// <summary>Not now (a dialog is open, a reload or open is running): tried again shortly.</summary>
    Postponed,

    /// <summary>
    /// The file was changed by another program and that change was not applied yet: not overwritten (the reload flow
    /// handles it first).
    /// </summary>
    DiskChanged,

    /// <summary>The save failed; shown as an error in the info bar.</summary>
    Failed,
}
