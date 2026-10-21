namespace DmxPro.App.Services;

/// <summary>Opens and closes the single live stage window (the stage plan in its own top-level window).</summary>
public interface IStageWindowService
{
    /// <summary>Gets whether the live stage window is open.</summary>
    bool IsOpen { get; }

    /// <summary>Opens the live stage window, or restores and activates it when it is already open.</summary>
    void Show();

    /// <summary>Closes the live stage window if it is open (the main window calls this on exit).</summary>
    void Close();
}
