namespace DmxPro.Engine.Runtime;

/// <summary>A pooled playing show (engine thread only).</summary>
internal sealed class ShowPlayer
{
    public bool InUse { get; set; }

    public ShowProgram Program { get; set; } = null!;

    public double PositionSeconds { get; set; }

    /// <summary>While true the position does not advance; the show keeps rendering at its frozen position.</summary>
    public bool Paused { get; set; }

    /// <summary>Start order of the show; LTP tie-breaker between shows.</summary>
    public long Seq { get; set; }
}
