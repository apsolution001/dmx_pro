namespace DmxPro.Core.Functions;

/// <summary>Curve of a <see cref="PulseFunction"/> between its minimum and maximum level.</summary>
public enum PulseShape
{
    /// <summary>Smooth breathing (raised cosine; starts at the minimum).</summary>
    Sine,

    /// <summary>Linear up, linear down (starts at the minimum).</summary>
    Triangle,
}
