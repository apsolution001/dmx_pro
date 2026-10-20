using System.Runtime.InteropServices;

namespace DmxPro.Output.Transport;

/// <summary>
/// Requests 1 ms Windows timer resolution while an output thread runs. With the default ~15.6 ms tick, waits of
/// 22.7 ms (44 Hz) land on 31 ms and the frame rate drops to ~32 Hz. No-op on other platforms.
/// </summary>
internal static class TimerResolution
{
    internal const uint PeriodMilliseconds = 1;

    /// <returns><c>true</c> if a matching <see cref="End"/> call is required.</returns>
    public static bool Begin()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return timeBeginPeriod(PeriodMilliseconds) == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Without winmm we still send, only with coarser timing.
            return false;
        }
    }

    public static void End(bool begun)
    {
        if (begun)
        {
            _ = timeEndPeriod(PeriodMilliseconds);
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
}
