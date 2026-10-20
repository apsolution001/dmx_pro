using System.Text.Json.Serialization;

namespace DmxPro.Core.Shows;

/// <summary>A function placed on a show track for a time range. References the show's own copy of the function.</summary>
public sealed class ShowItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FunctionId { get; set; }

    public double StartSeconds { get; set; }

    public double LengthSeconds { get; set; }

    public double FadeInSeconds { get; set; }

    public double FadeOutSeconds { get; set; }

    [JsonIgnore]
    public double EndSeconds => StartSeconds + LengthSeconds;

    /// <summary>True when start ≥ 0, length &gt; 0, fades ≥ 0 and fade in + fade out ≤ length (all finite).</summary>
    [JsonIgnore]
    public bool HasValidTiming =>
        double.IsFinite(StartSeconds) && StartSeconds >= 0
        && double.IsFinite(LengthSeconds) && LengthSeconds > 0
        && double.IsFinite(FadeInSeconds) && FadeInSeconds >= 0
        && double.IsFinite(FadeOutSeconds) && FadeOutSeconds >= 0
        && FadeInSeconds + FadeOutSeconds <= LengthSeconds;

    /// <summary>
    /// Sets the timing. Start must be ≥ 0 and length &gt; 0 (otherwise throws). Fades are clamped: negative or
    /// non-finite becomes 0, and when fade in + fade out exceeds the length, fade out is shortened first, then fade in.
    /// </summary>
    public void SetTiming(double startSeconds, double lengthSeconds, double fadeInSeconds = 0, double fadeOutSeconds = 0)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), startSeconds, "Start must be 0 or later.");
        }

        if (!double.IsFinite(lengthSeconds) || lengthSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthSeconds), lengthSeconds, "Length must be greater than 0.");
        }

        var fadeIn = Math.Clamp(double.IsFinite(fadeInSeconds) ? fadeInSeconds : 0, 0, lengthSeconds);
        var fadeOut = Math.Clamp(double.IsFinite(fadeOutSeconds) ? fadeOutSeconds : 0, 0, lengthSeconds - fadeIn);

        StartSeconds = startSeconds;
        LengthSeconds = lengthSeconds;
        FadeInSeconds = fadeIn;
        FadeOutSeconds = fadeOut;
    }
}
