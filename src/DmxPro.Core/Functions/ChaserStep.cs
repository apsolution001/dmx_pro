namespace DmxPro.Core.Functions;

/// <summary>
/// One step of a <see cref="ChaserFunction"/>: plays another function (usually a scene) for
/// <see cref="FadeInSeconds"/> + <see cref="HoldSeconds"/>, then fades it out over <see cref="FadeOutSeconds"/> while
/// the next step already fades in (crossfade).
/// </summary>
public sealed class ChaserStep
{
    public const double DefaultHoldSeconds = 1.0;

    /// <summary>The function this step plays (referenced, never copied).</summary>
    public Guid FunctionId { get; set; }

    /// <summary>Time from the step start until the function is fully in. Part of the step duration.</summary>
    public double FadeInSeconds { get; set; }

    /// <summary>Time the function stays fully in after its fade-in.</summary>
    public double HoldSeconds { get; set; } = DefaultHoldSeconds;

    /// <summary>Fade-out after the step ends; overlaps the next step.</summary>
    public double FadeOutSeconds { get; set; }

    public ChaserStep Clone() => (ChaserStep)MemberwiseClone();
}
