namespace DmxPro.Core.Functions;

/// <summary>All function types from the spec (§4). Each value has a concrete <see cref="LightFunction"/> model.</summary>
public enum FunctionType
{
    Scene,
    Chaser,
    Sequence,
    ColorCycle,
    Fade,
    Pulse,
    Flash,
    Strobe,
}
