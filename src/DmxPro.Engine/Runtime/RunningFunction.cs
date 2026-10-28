namespace DmxPro.Engine.Runtime;

/// <summary>A pooled running function instance (engine thread only).</summary>
internal sealed class RunningFunction
{
    public bool InUse { get; set; }

    public FunctionProgram Program { get; set; } = null!;

    public Guid FunctionId { get; set; }

    public EngineLayer Layer { get; set; }

    public float Intensity { get; set; }

    /// <summary>Speed factor of the function's own time (steps, cycles, rates); fades in/out stay real time.</summary>
    public float Speed { get; set; } = 1f;

    /// <summary>Strobe limits reported by the last render (for snapshots).</summary>
    public StrobeLimit StrobeLimit { get; set; }

    /// <summary>Fade level 0..1 (before intensity).</summary>
    public float Level { get; set; }

    public bool Stopping { get; set; }

    /// <summary>
    /// A preview of an unsaved editor draft (<see cref="DmxEngine.SetPreviewFunction"/>): its program is not part of the
    /// model, so a model refresh keeps it instead of re-binding it by id, and it starts and stops without fades.
    /// </summary>
    public bool IsDraft { get; set; }

    public double ElapsedSeconds { get; set; }

    /// <summary>Engine time when started (LTP order).</summary>
    public double Stamp { get; set; }

    public long Seq { get; set; }

    /// <summary>
    /// Advances the fade by <paramref name="dt"/> seconds and the function time by <paramref name="dt"/> × speed.
    /// Returns false once fully faded out, or when the function has finished by itself (single-shot chaser: its content
    /// has already ended, so it is released at once). <paramref name="context"/> carries the frame's strobe settings;
    /// its <see cref="RenderContext.Speed"/> is set to this function's speed.
    /// </summary>
    public bool Advance(double dt, RenderContext context)
    {
        ElapsedSeconds += dt * Speed;
        context.Speed = Speed;
        if (!Stopping && Program.HasEnded(ElapsedSeconds, context))
        {
            return false;
        }

        if (Stopping)
        {
            var fadeOut = Program.FadeOutSeconds;
            Level = fadeOut <= 0 ? 0f : (float)(Level - (dt / fadeOut));
            return Level > 0f;
        }

        var fadeIn = Program.FadeInSeconds;
        Level = fadeIn <= 0 ? 1f : (float)Math.Min(1.0, Level + (dt / fadeIn));
        return true;
    }
}
