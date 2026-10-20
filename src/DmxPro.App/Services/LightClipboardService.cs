using DmxPro.Core.Profiles;

namespace DmxPro.App.Services;

/// <summary>
/// The app's "copied light values" (in memory, never saved): copy a light on the Test page or in a scene editor, paste
/// it onto other lights there or on the other page. Kept separate from the Windows clipboard so pasting never depends
/// on text another program put there.
/// </summary>
public sealed class LightClipboardService
{
    /// <summary>The last copied light, or null.</summary>
    public CopiedLightValues? Current { get; private set; }

    public bool HasValues => Current is not null;

    /// <summary>Raised on the UI thread after <see cref="Current"/> changed.</summary>
    public event EventHandler? Changed;

    public void Copy(CopiedLightValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Current = values;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
