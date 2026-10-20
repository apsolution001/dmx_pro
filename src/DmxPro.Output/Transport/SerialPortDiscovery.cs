using Microsoft.Win32;

namespace DmxPro.Output.Transport;

/// <summary>Lists COM ports for the Enttec Pro port picker (same source Windows' own SerialPort uses).</summary>
public static class SerialPortDiscovery
{
    /// <summary>Port names such as <c>COM3</c>, sorted; empty on non-Windows or when none exist.</summary>
    public static IReadOnlyList<string> GetPortNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var valueName in key.GetValueNames())
        {
            if (key.GetValue(valueName) is string port && !string.IsNullOrWhiteSpace(port))
            {
                names.Add(port.Trim());
            }
        }

        names.Sort(ComparePortNames);
        return names;
    }

    /// <summary>COM2 before COM10.</summary>
    private static int ComparePortNames(string a, string b)
    {
        var numberA = TrailingNumber(a);
        var numberB = TrailingNumber(b);
        return numberA != numberB ? numberA.CompareTo(numberB) : string.CompareOrdinal(a, b);
    }

    private static int TrailingNumber(string name)
    {
        var start = name.Length;
        while (start > 0 && char.IsAsciiDigit(name[start - 1]))
        {
            start--;
        }

        return int.TryParse(name.AsSpan(start), out var number) ? number : int.MaxValue;
    }
}
