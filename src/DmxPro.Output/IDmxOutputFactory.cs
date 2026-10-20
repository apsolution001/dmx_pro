using DmxPro.Output.Configuration;

namespace DmxPro.Output;

/// <summary>Creates the output implementation for a configuration (replaceable in tests).</summary>
public interface IDmxOutputFactory
{
    IDmxOutput Create(OutputConfig config);
}
