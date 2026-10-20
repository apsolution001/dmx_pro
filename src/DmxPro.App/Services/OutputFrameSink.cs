using DmxPro.Engine;
using DmxPro.Output;

namespace DmxPro.App.Services;

/// <summary>
/// Bridges the engine to the outputs: every merged universe frame goes to <see cref="OutputManager.Submit"/>, which
/// drops it while output is disabled (safety rule 1). Runs on the engine thread; allocation-free and non-blocking.
/// </summary>
public sealed class OutputFrameSink : IFrameSink
{
    private readonly OutputManager _output;

    public OutputFrameSink(OutputManager output)
    {
        _output = output;
    }

    public void OnFrame(int universe, ReadOnlySpan<byte> data) => _output.Submit(universe, data);
}
