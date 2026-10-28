using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A function compiled against the current patch: everything resolved to flat channel indexes so rendering allocates
/// nothing. Rendering is a pure function of the elapsed time, so a show can seek anywhere and a chaser can play a
/// function inside a step. Each function type adds a subclass and a case in <see cref="FunctionCompiler"/>.
/// </summary>
internal abstract class FunctionProgram
{
    protected FunctionProgram(Guid id, double fadeInSeconds, double fadeOutSeconds)
    {
        Id = id;
        FadeInSeconds = SanitizeSeconds(fadeInSeconds);
        FadeOutSeconds = SanitizeSeconds(fadeOutSeconds);
    }

    public Guid Id { get; }

    /// <summary>Fade-in used when the function is started directly (show items use their own fades).</summary>
    public double FadeInSeconds { get; }

    /// <summary>Fade-out used when the function is stopped directly.</summary>
    public double FadeOutSeconds { get; }

    /// <summary>
    /// True once the function has finished by itself at function time <paramref name="elapsedSeconds"/> (single-shot
    /// chaser, whose length depends on the speed limit in <paramref name="context"/>); a running instance is then
    /// released. The default runs until stopped. Allocation-free (engine thread).
    /// </summary>
    /// <param name="elapsedSeconds">Function time since it started (already scaled by its speed).</param>
    /// <param name="context">Frame settings (strobe safety, speed).</param>
    public virtual bool HasEnded(double elapsedSeconds, RenderContext context) => false;

    /// <summary>Writes this function's values into <paramref name="layer"/>.</summary>
    /// <param name="elapsedSeconds">Function time since it started (already scaled by its speed).</param>
    /// <param name="weight">Fade level × intensity, 0..1.</param>
    /// <param name="layer">Target layer accumulator.</param>
    /// <param name="stamp">Engine time the function started (LTP order).</param>
    /// <param name="seq">Tie-breaker for equal stamps (LTP order).</param>
    /// <param name="context">Frame settings (strobe safety, speed) and strobe-limit reporting.</param>
    public abstract void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context);

    /// <summary>
    /// Longest single time a program uses (fade, hold, step, duration): the editors' limit. Damaged or hand-edited files
    /// can hold any finite double; without the cap sums such as a chaser's pass length overflow to infinity.
    /// </summary>
    public const double MaxSeconds = FunctionEditing.MaxSeconds;

    /// <summary>Finite and ≥ 0, else 0; capped at <see cref="MaxSeconds"/>.</summary>
    protected static double SanitizeSeconds(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? Math.Min(seconds, MaxSeconds) : 0;
}
