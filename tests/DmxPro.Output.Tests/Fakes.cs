using System.Collections.Concurrent;
using DmxPro.Output.Transport;

namespace DmxPro.Output.Tests;

internal sealed class FakeSerialPort : ISerialPortTransport
{
    public ConcurrentQueue<byte[]> Writes { get; } = new();

    public string? OpenedPort { get; private set; }

    public int OpenAttempts;

    public Func<Exception?> OpenFailure { get; set; } = () => null;

    public Func<Exception?> WriteFailure { get; set; } = () => null;

    public void Open(string portName)
    {
        Interlocked.Increment(ref OpenAttempts);
        if (OpenFailure() is { } e)
        {
            throw e;
        }

        OpenedPort = portName;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (WriteFailure() is { } e)
        {
            throw e;
        }

        Writes.Enqueue(data.ToArray());
    }

    public void Close() => OpenedPort = null;

    public void Dispose() => Close();
}

internal sealed class FakeFtdiPort : IFtdiPort
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<byte[]> Writes { get; } = new();

    public void Open(string? serialNumber, int deviceIndex) => Calls.Enqueue($"open:{serialNumber ?? deviceIndex.ToString()}");

    public void SetBreak(bool on) => Calls.Enqueue(on ? "break-on" : "break-off");

    public void Write(ReadOnlySpan<byte> data)
    {
        Calls.Enqueue($"write:{data.Length}");
        Writes.Enqueue(data.ToArray());
    }

    public void Close() => Calls.Enqueue("close");

    public void Dispose()
    {
    }
}
