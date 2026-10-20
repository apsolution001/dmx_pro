using System.Globalization;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>Shared option lists and readouts of the step-based editors (chaser, sequence).</summary>
public static class EditorTiming
{
    public static IReadOnlyList<EditorOption<RunOrder>> RunOrders { get; } =
    [
        new(RunOrder.Loop, "Loop", "After the last step, start again at the first; runs until stopped"),
        new(RunOrder.SingleShot, "Single shot", "Play every step once, then stop"),
        new(RunOrder.PingPong, "Ping-pong", "First to last and back again; runs until stopped"),
    ];

    public static IReadOnlyList<EditorOption<RunDirection>> Directions { get; } =
    [
        new(RunDirection.Forward, "Forward", "First step to last step"),
        new(RunDirection.Backward, "Backward", "Last step to first step"),
    ];

    /// <summary>"Cycle: 6.5 s" / "Runs once: 7 s" / "There and back: 10 s"; "No steps yet" when empty.</summary>
    public static string CycleText(int stepCount, double seconds, RunOrder order)
    {
        if (stepCount == 0)
        {
            return "No steps yet";
        }

        var time = FormatDuration(seconds);
        return order switch
        {
            RunOrder.SingleShot => $"Runs once: {time}",
            RunOrder.PingPong => $"There and back: {time}",
            _ => $"Cycle: {time}",
        };
    }

    /// <summary>"6.5 s", "2 min 5 s".</summary>
    public static string FormatDuration(double seconds)
    {
        if (seconds < 60)
        {
            return seconds.ToString("0.##", CultureInfo.CurrentCulture) + " s";
        }

        var minutes = (int)(seconds / 60);
        var rest = seconds - (minutes * 60);
        return $"{minutes} min {rest.ToString("0.#", CultureInfo.CurrentCulture)} s";
    }
}
