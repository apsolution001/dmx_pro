using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A compiled chaser or sequence: steps laid out on a timeline in play order (direction and ping-pong already
/// unrolled into "slots"). Slot k starts when slot k-1's fade in + hold is over; a slot fades in over its fade-in, holds,
/// then fades out over its fade-out while the next slot already plays (crossfade). Loop and ping-pong repeat the slot
/// list forever; single shot plays it once and finishes after the last fade-out.
/// </summary>
/// <remarks>
/// Rendering is a pure function of time, so shows can seek and nested programs get their own step time. The slots
/// playing at a time are combined by <see cref="StepMix"/>: a channel shared by the outgoing and the incoming step blends
/// linearly over the incoming fade-in (see the rule there).
/// <para>
/// Speed limit (safety rule 8): at play time every slot lasts at least <see cref="RenderContext.LimitSeconds"/>
/// (from <see cref="ChaserSpeedLimit"/>, converted to function time with the speed), so a fast chaser cannot flash
/// faster than the global strobe settings allow. Stretched timelines are computed into a few preallocated cache entries
/// (no allocation in the tick; several instances at different speeds do not evict each other); stored step times are
/// untouched. A running instance moves to a new limit with <see cref="Rebase"/>, which keeps its step, so a speed fader
/// only changes the rate from then on. Programs are rendered only on the engine thread, one call at a time, so the cache
/// needs no locking.
/// </para>
/// </remarks>
internal sealed class StepProgram : FunctionProgram
{
    /// <summary>Shortest step (fade in + hold) played; shorter steps are stretched to this.</summary>
    public const double MinStepSeconds = 0.05;

    /// <summary>Upper bound of earlier slots checked for a still-running fade-out (keeps a tick's work bounded).</summary>
    private const int MaxTailSlots = 256;

    /// <summary>Stretched timelines kept per program (one per distinct limit in use).</summary>
    private const int CachedTimelines = 4;

    private readonly FunctionProgram?[] _programs;
    private readonly double[] _length;
    private readonly double[] _fadeIn;
    private readonly double[] _fadeOut;
    private readonly double _maxFadeOut;
    private readonly double _shortest = double.PositiveInfinity;
    private readonly bool _repeat;
    private readonly int _mixDepth;
    private readonly Timeline _stored;
    private readonly Timeline[] _limited;
    private int _nextLimited;

    /// <param name="id">Function id.</param>
    /// <param name="steps">Steps in edit order; a null program keeps its time but shows nothing (missing reference).</param>
    /// <param name="order">Run order.</param>
    /// <param name="direction">Run direction.</param>
    public StepProgram(Guid id, IReadOnlyList<Step> steps, RunOrder order, RunDirection direction)
        : base(id, 0, 0)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var playOrder = PlayOrder(steps.Count, order, direction);
        var n = playOrder.Length;
        _programs = new FunctionProgram?[n];
        _length = new double[n];
        _fadeIn = new double[n];
        _fadeOut = new double[n];

        var deepest = 0;
        for (var k = 0; k < n; k++)
        {
            var step = steps[playOrder[k]];
            var fadeIn = SanitizeSeconds(step.FadeInSeconds);
            _programs[k] = step.Program;
            _fadeIn[k] = fadeIn;
            _length[k] = Math.Max(fadeIn + SanitizeSeconds(step.HoldSeconds), MinStepSeconds);
            _fadeOut[k] = SanitizeSeconds(step.FadeOutSeconds);
            _maxFadeOut = Math.Max(_maxFadeOut, _fadeOut[k]);
            _shortest = Math.Min(_shortest, _length[k]);
            deepest = Math.Max(deepest, step.Program?.MixDepth ?? 0);
        }

        _mixDepth = deepest + 1;

        // Step times are capped (SanitizeSeconds), so a non-finite pass only comes from absurd step counts: such a pass
        // plays once instead of computing NaN slots (see Fill).
        _repeat = order != RunOrder.SingleShot;
        _stored = new Timeline(n);
        Fill(_stored, 0);
        _limited = new Timeline[CachedTimelines];
        for (var i = 0; i < _limited.Length; i++)
        {
            _limited[i] = new Timeline(n);
        }
    }

    public override int MixDepth => _mixDepth;

    /// <summary>Single shot: finished once the last slot's fade-out is over on the (possibly stretched) timeline.</summary>
    public override bool HasEnded(double elapsedSeconds, RenderContext context)
    {
        var timeline = TimelineFor(context.LimitSeconds);
        return !timeline.Repeat && _programs.Length > 0 && double.IsFinite(timeline.Total) && elapsedSeconds >= timeline.Total;
    }

    public override long StepSignature(double elapsedSeconds, RenderContext context)
    {
        var n = _programs.Length;
        var t = elapsedSeconds;
        if (n == 0 || !(t >= 0) || double.IsInfinity(t))
        {
            return 0;
        }

        var timeline = TimelineFor(context.LimitSeconds);
        if (!timeline.Repeat && t >= timeline.Total)
        {
            return -1;
        }

        var current = CurrentSlot(timeline, t);
        var inner = 0L;
        if ((current < n || timeline.Repeat) && _programs[(int)(current % n)] is { } program)
        {
            inner = program.StepSignature(t - SlotStart(timeline, current), context);
        }

        // Hash-combine (not a sum: "outer step +1, inner step −1" must not cancel out).
        unchecked
        {
            var signature = (current + 1) * -7046029254386353131L; // 0x9E3779B97F4A7C15
            signature ^= inner + 0x632BE59BD9B4E019L + (signature << 6) + (long)((ulong)signature >> 2);
            return signature;
        }
    }

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        var n = _programs.Length;
        var t = elapsedSeconds;
        if (n == 0 || !(t >= 0) || double.IsInfinity(t))
        {
            return;
        }

        var timeline = TimelineFor(context.LimitSeconds);
        if (timeline.Stretched)
        {
            context.StrobeLimit |= StrobeLimit.RateLimited;
        }

        if (!timeline.Repeat && t >= timeline.Total)
        {
            return;
        }

        // Past the last slot of a single shot only fade-out tails remain: "current" is one past the end.
        var current = CurrentSlot(timeline, t);
        var hasCurrent = current < n || timeline.Repeat;
        var currentStart = hasCurrent ? SlotStart(timeline, current) : t;
        var fadeInLevel = 1.0;
        if (hasCurrent)
        {
            var fadeIn = _fadeIn[(int)(current % n)];
            fadeInLevel = fadeIn > 0 ? Math.Clamp((t - currentStart) / fadeIn, 0, 1) : 1;
        }

        // While the current slot fades in, older slots are needed as the start point of shared channels, even when their
        // own fade-out is already over (or 0): back to the slots still fading when the current slot started.
        var blending = hasCurrent && fadeInLevel < 1 && current > 0;
        var horizon = blending ? currentStart : t;
        var first = current;
        for (var i = 0; i < MaxTailSlots && first > 0; i++)
        {
            var previous = first - 1;
            var end = SlotStart(timeline, previous) + timeline.Length[previous % n];
            if (horizon - end >= _maxFadeOut && !(blending && previous == current - 1))
            {
                break;
            }

            first = previous;
        }

        // Oldest first: newer slots take over the channels they share with older ones.
        var mix = layer.Mix;
        for (var slot = first; slot < current; slot++)
        {
            var k = (int)(slot % n);
            var start = SlotStart(timeline, slot);
            var end = start + timeline.Length[k];
            var level = TailLevel(t - end, _fadeOut[k]);
            var from = !blending ? 0 : slot == current - 1 ? 1 : TailLevel(currentStart - end, _fadeOut[k]);
            if (level <= 0 && from <= 0)
            {
                continue;
            }

            _programs[k]?.Render(t - start, 1f, mix.Slot, stamp, seq, context);
            mix.FoldTail((float)level, (float)from);
        }

        if (hasCurrent)
        {
            _programs[(int)(current % n)]?.Render(t - currentStart, 1f, mix.Slot, stamp, seq, context);
            mix.FoldCurrent((float)fadeInLevel);
        }

        mix.Emit(layer, weight, stamp, seq);
    }

    /// <summary>
    /// Function time in <paramref name="to"/> (played with limit <paramref name="toLimit"/>) at the same step as
    /// <paramref name="elapsedSeconds"/> in <paramref name="from"/> (played with <paramref name="fromLimit"/>): same pass and
    /// slot, same time into the fade-in, same fraction of the hold. Used when a running instance's limit (speed, strobe
    /// settings) or program (refresh, draft edit) changes, so it continues where it was instead of jumping to the step the
    /// new timeline has at the old time. Non-chasers keep their time. Allocation-free.
    /// </summary>
    public static double Rebase(FunctionProgram from, double fromLimit, FunctionProgram to, double toLimit, double elapsedSeconds)
    {
        if (from is not StepProgram source || to is not StepProgram target
            || !(elapsedSeconds >= 0) || double.IsInfinity(elapsedSeconds)
            || source._programs.Length == 0 || target._programs.Length == 0)
        {
            return elapsedSeconds;
        }

        var sourceTimeline = source.TimelineFor(fromLimit);
        if (ReferenceEquals(source, target) && ReferenceEquals(sourceTimeline, target.TimelineFor(toLimit)))
        {
            return elapsedSeconds;
        }

        // Read the position before the target timeline is fetched (it may reuse a cache entry of the same program).
        var n = source._programs.Length;
        var current = source.CurrentSlot(sourceTimeline, elapsedSeconds);
        var ended = !sourceTimeline.Repeat && current >= n;
        var pass = ended ? 0 : current / n;
        var k = ended ? 0 : (int)(current % n);
        var offset = elapsedSeconds - (ended ? sourceTimeline.Period : source.SlotStart(sourceTimeline, current));
        var length = ended ? 0 : sourceTimeline.Length[k];
        var fadeIn = ended ? 0 : source._fadeIn[k];

        var targetTimeline = target.TimelineFor(toLimit);
        if (ended)
        {
            return targetTimeline.Repeat ? elapsedSeconds : targetTimeline.Period + offset;
        }

        var m = target._programs.Length;
        var slot = (targetTimeline.Repeat ? pass * m : 0) + Math.Min(k, m - 1);
        var targetK = (int)(slot % m);
        var targetLength = targetTimeline.Length[targetK];
        var targetFadeIn = target._fadeIn[targetK];
        double into;
        if (offset < fadeIn)
        {
            into = Math.Min(offset, targetFadeIn);
        }
        else
        {
            var hold = length - fadeIn;
            var fraction = hold > 0 ? Math.Clamp((offset - fadeIn) / hold, 0, 1) : 1;
            into = targetFadeIn + (fraction * (targetLength - targetFadeIn));
        }

        // Stay inside the slot: reaching its end would be a step change caused by the re-timing itself.
        into = Math.Clamp(into, 0, Math.BitDecrement(targetLength));
        return target.SlotStart(targetTimeline, slot) + into;
    }

    /// <summary>Fade-out level of a finished slot <paramref name="sinceEnd"/> seconds after its end (0 without fade-out).</summary>
    private static double TailLevel(double sinceEnd, double fadeOut) =>
        fadeOut > 0 ? Math.Clamp(1 - (sinceEnd / fadeOut), 0, 1) : 0;

    /// <summary>Step indexes in play order: forward/backward, ping-pong without repeating the end steps.</summary>
    private static int[] PlayOrder(int count, RunOrder order, RunDirection direction)
    {
        var forward = new int[count];
        for (var i = 0; i < count; i++)
        {
            forward[i] = direction == RunDirection.Backward ? count - 1 - i : i;
        }

        if (order != RunOrder.PingPong || count < 3)
        {
            return forward;
        }

        var result = new int[(2 * count) - 2];
        forward.CopyTo(result, 0);
        for (var i = 1; i < count - 1; i++)
        {
            result[count - 1 + i] = forward[count - 1 - i];
        }

        return result;
    }

    /// <summary>
    /// Timeline for a minimum slot length (function time): the stored one when no slot is shorter (or the limit is not a
    /// number), else a stretched one from the cache, recomputed in place (round robin) on a miss. Allocation-free.
    /// </summary>
    private Timeline TimelineFor(double limitSeconds)
    {
        if (!(limitSeconds > _shortest))
        {
            return _stored;
        }

        for (var i = 0; i < _limited.Length; i++)
        {
            // Exact compare on purpose: the key is the value last computed for, not a measured quantity.
            if (_limited[i].Limit.Equals(limitSeconds))
            {
                return _limited[i];
            }
        }

        var timeline = _limited[_nextLimited];
        _nextLimited = (_nextLimited + 1) % _limited.Length;
        Fill(timeline, limitSeconds);
        return timeline;
    }

    private void Fill(Timeline timeline, double limitSeconds)
    {
        var time = 0.0;
        var total = 0.0;
        for (var k = 0; k < _length.Length; k++)
        {
            var length = Math.Max(_length[k], limitSeconds);
            timeline.Start[k] = time;
            timeline.Length[k] = length;
            total = Math.Max(total, time + length + _fadeOut[k]);
            time += length;
        }

        timeline.Limit = limitSeconds;
        timeline.Period = time;
        timeline.Total = total;
        timeline.Repeat = _repeat && double.IsFinite(time);
        timeline.Stretched = limitSeconds > _shortest;
    }

    /// <summary>Absolute slot (pass × slots + slot) playing at <paramref name="t"/>; one past the end once a single shot is over.</summary>
    private long CurrentSlot(Timeline timeline, double t)
    {
        if (timeline.Repeat)
        {
            var cycle = (long)Math.Floor(t / timeline.Period);
            return (cycle * _programs.Length) + SlotAt(timeline, t - (cycle * timeline.Period));
        }

        return t >= timeline.Period ? _programs.Length : SlotAt(timeline, t);
    }

    /// <summary>Slot within one pass containing time <paramref name="timeInPass"/> (0 ≤ t &lt; period).</summary>
    private static int SlotAt(Timeline timeline, double timeInPass)
    {
        var k = timeline.Start.Length - 1;
        while (k > 0 && timeline.Start[k] > timeInPass)
        {
            k--;
        }

        return k;
    }

    private double SlotStart(Timeline timeline, long slot)
    {
        // The first pass needs no period (keeps 0 × period out of the sum when the period is not finite).
        var n = _programs.Length;
        return slot < n ? timeline.Start[(int)slot] : ((slot / n) * timeline.Period) + timeline.Start[(int)(slot % n)];
    }

    /// <summary>One step before unrolling.</summary>
    /// <param name="Program">What the step plays; null = nothing (missing function or reference loop).</param>
    /// <param name="FadeInSeconds">Fade-in at the step start (part of the step duration).</param>
    /// <param name="HoldSeconds">Hold after the fade-in.</param>
    /// <param name="FadeOutSeconds">Fade-out after the step, overlapping the next.</param>
    internal readonly record struct Step(FunctionProgram? Program, double FadeInSeconds, double HoldSeconds, double FadeOutSeconds);

    /// <summary>Slot start times and lengths for one minimum slot length (engine thread only).</summary>
    private sealed class Timeline
    {
        public Timeline(int slots)
        {
            Start = new double[slots];
            Length = new double[slots];
        }

        public double[] Start { get; }

        public double[] Length { get; }

        /// <summary>Minimum slot length this was computed for (0 = stored times); NaN = empty cache entry.</summary>
        public double Limit { get; set; } = double.NaN;

        public double Period { get; set; }

        public double Total { get; set; }

        public bool Repeat { get; set; }

        public bool Stretched { get; set; }
    }
}
