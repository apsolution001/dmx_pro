using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A compiled chaser or sequence: steps laid out on a timeline in play order (direction and ping-pong already
/// unrolled into "slots"). Slot k starts when slot k-1's fade in + hold is over; a slot fades in over its fade-in, holds,
/// then fades out over its fade-out while the next slot already plays (crossfade). Loop and ping-pong repeat the slot
/// list forever; single shot plays it once and finishes after the last fade-out.
/// </summary>
/// <remarks>Rendering is a pure function of time, so shows can seek and nested programs get their own step time.</remarks>
internal sealed class StepProgram : FunctionProgram
{
    /// <summary>Shortest step (fade in + hold) played; shorter steps are stretched to this.</summary>
    public const double MinStepSeconds = 0.05;

    /// <summary>Upper bound of earlier slots checked for a still-running fade-out (keeps a tick's work bounded).</summary>
    private const int MaxTailSlots = 256;

    private readonly FunctionProgram?[] _programs;
    private readonly double[] _start;
    private readonly double[] _length;
    private readonly double[] _fadeIn;
    private readonly double[] _fadeOut;
    private readonly double _period;
    private readonly double _total;
    private readonly double _maxFadeOut;
    private readonly bool _repeat;

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
        _start = new double[n];
        _length = new double[n];
        _fadeIn = new double[n];
        _fadeOut = new double[n];

        var time = 0.0;
        for (var k = 0; k < n; k++)
        {
            var step = steps[playOrder[k]];
            var fadeIn = SanitizeSeconds(step.FadeInSeconds);
            _programs[k] = step.Program;
            _start[k] = time;
            _fadeIn[k] = fadeIn;
            _length[k] = Math.Max(fadeIn + SanitizeSeconds(step.HoldSeconds), MinStepSeconds);
            _fadeOut[k] = SanitizeSeconds(step.FadeOutSeconds);
            _maxFadeOut = Math.Max(_maxFadeOut, _fadeOut[k]);
            _total = Math.Max(_total, time + _length[k] + _fadeOut[k]);
            time += _length[k];
        }

        // Step times are capped (SanitizeSeconds), so this only guards absurd step counts: a pass that does not fit in a
        // double plays once instead of computing NaN slots.
        _period = time;
        _repeat = order != RunOrder.SingleShot && double.IsFinite(_period);
    }

    public override double DurationSeconds => _repeat || _programs.Length == 0 || !double.IsFinite(_total) ? double.PositiveInfinity : _total;

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        var n = _programs.Length;
        var t = elapsedSeconds;
        if (n == 0 || !(t >= 0) || double.IsInfinity(t))
        {
            return;
        }

        long current;
        if (_repeat)
        {
            var cycle = (long)Math.Floor(t / _period);
            current = (cycle * n) + SlotAt(t - (cycle * _period));
        }
        else
        {
            if (t >= _total)
            {
                return;
            }

            // Past the last slot only fade-out tails remain: "current" is one past the end.
            current = t >= _period ? n : SlotAt(t);
        }

        // Earlier slots whose fade-out still runs, oldest first so the current slot is written last (wins LTP).
        var first = current;
        for (var i = 0; i < MaxTailSlots && first > 0; i++)
        {
            var previous = first - 1;
            var end = SlotStart(previous) + _length[previous % n];
            if (t - end >= _maxFadeOut)
            {
                break;
            }

            first = previous;
        }

        for (var slot = first; slot < current; slot++)
        {
            var k = (int)(slot % n);
            var sinceStart = t - SlotStart(slot);
            var fadeOut = _fadeOut[k];
            var level = fadeOut > 0 ? 1 - ((sinceStart - _length[k]) / fadeOut) : 0;
            if (level > 0)
            {
                _programs[k]?.Render(sinceStart, weight * (float)Math.Min(level, 1), layer, stamp, seq, context);
            }
        }

        if (current < n || _repeat)
        {
            var k = (int)(current % n);
            var sinceStart = t - SlotStart(current);
            var fadeIn = _fadeIn[k];
            var level = fadeIn > 0 ? Math.Min(1, sinceStart / fadeIn) : 1;
            if (level > 0)
            {
                _programs[k]?.Render(sinceStart, weight * (float)level, layer, stamp, seq, context);
            }
        }
    }

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

    /// <summary>Slot within one pass containing time <paramref name="timeInPass"/> (0 ≤ t &lt; period).</summary>
    private int SlotAt(double timeInPass)
    {
        var k = _start.Length - 1;
        while (k > 0 && _start[k] > timeInPass)
        {
            k--;
        }

        return k;
    }

    private double SlotStart(long slot)
    {
        // The first pass needs no period (keeps 0 × period out of the sum when the period is not finite).
        var n = _programs.Length;
        return slot < n ? _start[(int)slot] : ((slot / n) * _period) + _start[(int)(slot % n)];
    }

    /// <summary>One step before unrolling.</summary>
    /// <param name="Program">What the step plays; null = nothing (missing function or reference loop).</param>
    /// <param name="FadeInSeconds">Fade-in at the step start (part of the step duration).</param>
    /// <param name="HoldSeconds">Hold after the fade-in.</param>
    /// <param name="FadeOutSeconds">Fade-out after the step, overlapping the next.</param>
    internal readonly record struct Step(FunctionProgram? Program, double FadeInSeconds, double HoldSeconds, double FadeOutSeconds);
}
