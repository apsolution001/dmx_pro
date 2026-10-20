namespace DmxPro.Core.Stage;

/// <summary>
/// What a fixture looks like right now on the virtual stage: the colour of its light (already mixed from all colour
/// channels, full brightness) and how bright it is. Black/0 means dark.
/// </summary>
/// <param name="R">Red of the emitted colour, 0–255.</param>
/// <param name="G">Green of the emitted colour, 0–255.</param>
/// <param name="B">Blue of the emitted colour, 0–255.</param>
/// <param name="Intensity">Overall brightness 0..1 (dimmer × strobe phase).</param>
/// <param name="IsStrobing">The fixture's strobe channel is in a strobing range (the view may show a strobe mark).</param>
public readonly record struct StageLightState(byte R, byte G, byte B, float Intensity, bool IsStrobing)
{
    /// <summary>Dark.</summary>
    public static StageLightState Off => default;
}
