using System.Globalization;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>Plain-language numbers of the pulse, flash and strobe editors ("2 s", "30 times a minute", "5 per second").</summary>
public static class EffectEditorFormat
{
    /// <summary>Two numbers typed or chosen in an editor count as the same (preset matching).</summary>
    public static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.0005;

    /// <summary>"2 s", "0.25 s", "1 min 30 s".</summary>
    public static string Seconds(double seconds) => EditorTiming.FormatDuration(seconds);

    /// <summary>"5", "2.5", "0.1".</summary>
    public static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>"30 times a minute" for a 2 s breath; "less than once a minute" for very slow ones.</summary>
    public static string TimesPerMinute(double periodSeconds)
    {
        if (!double.IsFinite(periodSeconds) || periodSeconds <= 0)
        {
            return string.Empty;
        }

        var perMinute = 60.0 / periodSeconds;
        if (perMinute < 1)
        {
            return "less than once a minute";
        }

        var rounded = Math.Round(perMinute, perMinute < 10 ? 1 : 0);
        return rounded == 1 ? "once a minute" : $"{Number(rounded)} times a minute";
    }

    /// <summary>"1 flash per second", "5 flashes per second".</summary>
    public static string FlashesPerSecond(double rateHz) =>
        NearlyEqual(rateHz, 1) ? "1 flash per second" : $"{Number(rateHz)} flashes per second";
}
