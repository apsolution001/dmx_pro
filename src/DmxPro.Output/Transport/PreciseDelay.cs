using System.Diagnostics;

namespace DmxPro.Output.Transport;

/// <summary>Microsecond busy-wait for DMX break / mark-after-break timing (sleeps are far too coarse).</summary>
internal static class PreciseDelay
{
    public static void Microseconds(int microseconds)
    {
        var target = Stopwatch.GetTimestamp() + (microseconds * Stopwatch.Frequency / 1_000_000);
        var spinner = default(SpinWait);
        while (Stopwatch.GetTimestamp() < target)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
        }
    }
}
