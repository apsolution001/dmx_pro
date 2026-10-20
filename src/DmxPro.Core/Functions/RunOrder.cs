namespace DmxPro.Core.Functions;

/// <summary>How a step-based function (chaser, sequence) runs through its steps.</summary>
public enum RunOrder
{
    /// <summary>After the last step, start again at the first; runs until stopped.</summary>
    Loop,

    /// <summary>Play every step once, then stop by itself.</summary>
    SingleShot,

    /// <summary>Play first → last → first … (the end steps are not repeated); runs until stopped.</summary>
    PingPong,
}
