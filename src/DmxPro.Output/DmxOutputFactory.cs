using DmxPro.Output.Configuration;

namespace DmxPro.Output;

/// <summary>Default factory mapping each configuration type to its output.</summary>
public sealed class DmxOutputFactory : IDmxOutputFactory
{
    public IDmxOutput Create(OutputConfig config) => config switch
    {
        ArtNetOutputConfig => new ArtNetOutput(),
        SacnOutputConfig => new SacnOutput(),
        EnttecProOutputConfig => new EnttecProOutput(),
        OpenDmxOutputConfig => new OpenDmxOutput(),
        VirtualOutputConfig => new VirtualOutput(),
        _ => throw new ArgumentException($"Unknown output configuration {config.GetType().Name}.", nameof(config)),
    };
}
