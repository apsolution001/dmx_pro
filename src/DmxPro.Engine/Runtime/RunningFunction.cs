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

    public double ElapsedSeconds { get; private set; }

    /// <summary>Engine time when started (LTP order).</summary>
    public double Stamp { get; set; }

    public long Seq { get; set; }

    /// <summary>
    /// Chaser speed limit (function time) this instance's time is laid out for; NaN until first advanced at a speed
    /// above 0. Kept while paused (speed 0), so a paused chaser does not jump.
    /// </summary>
    public double TimelineLimit { get; private set; } = double.NaN;

    /// <summary>Limit to render with (<see cref="RenderContext.LimitSeconds"/>).</summary>
    public double RenderLimit => double.IsNaN(TimelineLimit) ? 0 : TimelineLimit;

    // Step-change backstop (safety rule 8): the step last shown, when it changed (engine time) and the last function
    // time it was shown at.
    private long _stepSignature;
    private bool _hasStepSignature;
    private double _stepChangedAt;
    private double _heldElapsed;

    /// <summary>Starts the function's own time from 0 (a new instance) at engine time <paramref name="now"/>.</summary>
    public void ResetTime(double now)
    {
        ElapsedSeconds = 0;
        TimelineLimit = double.NaN;
        _hasStepSignature = false;
        _stepSignature = 0;
        _stepChangedAt = now;
        _heldElapsed = 0;
    }

    /// <summary>
    /// Plays <paramref name="program"/> from now on (refresh, draft edit). A chaser continues at the same step instead of
    /// jumping to whatever the new step times have at the old time.
    /// </summary>
    public void SwapProgram(FunctionProgram program)
    {
        if (!ReferenceEquals(program, Program) && !double.IsNaN(TimelineLimit))
        {
            ElapsedSeconds = StepProgram.Rebase(Program, TimelineLimit, program, TimelineLimit, ElapsedSeconds);
            _heldElapsed = StepProgram.Rebase(Program, TimelineLimit, program, TimelineLimit, _heldElapsed);
        }

        Program = program;
    }

    /// <summary>
    /// Function time to render at <paramref name="now"/>. A step change (<see cref="FunctionProgram.StepSignature"/>) less
    /// than <paramref name="minStepRealSeconds"/> of real time after the previous one is held back: the function time stays
    /// where the old step was last shown until the minimum has passed. Backstop for safety rule 8 against anything that
    /// re-times a running chaser (speed fader, strobe settings, draft edits, a nested chaser restarted by its parent step).
    /// <see cref="RenderContext.LimitSeconds"/> must already be set for this instance. Allocation-free.
    /// </summary>
    public double GuardStepChange(double now, double minStepRealSeconds, RenderContext context)
    {
        // Ticks and engine time accumulate rounding; a change exactly one minimum later must not be held for a frame.
        const double Tolerance = 1e-9;
        var signature = Program.StepSignature(ElapsedSeconds, context);
        if (!_hasStepSignature || signature == _stepSignature)
        {
            _hasStepSignature = true;
            _stepSignature = signature;
            _heldElapsed = ElapsedSeconds;
            return ElapsedSeconds;
        }

        if (now - _stepChangedAt >= minStepRealSeconds - Tolerance)
        {
            _stepSignature = signature;
            _stepChangedAt = now;
            _heldElapsed = ElapsedSeconds;
            return ElapsedSeconds;
        }

        // Too soon: freeze the function time (the chaser resumes from here, it does not skip the held time).
        ElapsedSeconds = _heldElapsed;
        return _heldElapsed;
    }

    /// <summary>
    /// Advances the fade by <paramref name="dt"/> seconds and the function time by <paramref name="dt"/> × speed.
    /// Returns false once fully faded out, or when the function has finished by itself (single-shot chaser: its content
    /// has already ended, so it is released at once). <paramref name="context"/> carries the frame's strobe settings;
    /// its <see cref="RenderContext.Speed"/> is set to this function's speed and its
    /// <see cref="RenderContext.LimitSeconds"/> to this instance's limit. When the limit changed (speed, strobe settings)
    /// the function time is first re-based to the same step, so the change only alters the rate from now on.
    /// </summary>
    public bool Advance(double dt, RenderContext context)
    {
        context.Speed = Speed;
        var limit = context.MinStepSeconds;
        if (!double.IsNaN(limit))
        {
            if (!double.IsNaN(TimelineLimit) && !limit.Equals(TimelineLimit))
            {
                ElapsedSeconds = StepProgram.Rebase(Program, TimelineLimit, Program, limit, ElapsedSeconds);
                _heldElapsed = StepProgram.Rebase(Program, TimelineLimit, Program, limit, _heldElapsed);
            }

            TimelineLimit = limit;
        }

        context.LimitSeconds = RenderLimit;
        ElapsedSeconds += dt * Speed;
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
