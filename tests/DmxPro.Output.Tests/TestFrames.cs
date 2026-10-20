namespace DmxPro.Output.Tests;

internal static class TestFrames
{
    /// <summary>0, 1, 2, … wrapping at 256.</summary>
    public static byte[] Ramp(int length, int start = 0) =>
        Enumerable.Range(start, length).Select(i => (byte)i).ToArray();

    public static byte[] Filled(byte value, int length = 512) => Enumerable.Repeat(value, length).ToArray();

    /// <summary>Polls <paramref name="condition"/> until true or the timeout elapses.</summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
