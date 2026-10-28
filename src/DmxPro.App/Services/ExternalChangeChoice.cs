namespace DmxPro.App.Services;

/// <summary>Answer to "the project file was changed outside DMX Pro" while there are unsaved changes.</summary>
public enum ExternalChangeChoice
{
    /// <summary>Apply the file's content (the unsaved changes stay reachable with Ctrl+Z).</summary>
    Reload,

    /// <summary>Keep the changes made in the app; the next save overwrites the file.</summary>
    KeepMine,
}
