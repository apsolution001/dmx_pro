using DmxPro.Core.Validation;

namespace DmxPro.Output.Configuration;

/// <summary>Loopback output: keeps the last transmitted frame per universe (DMX monitor, tests, no hardware).</summary>
public sealed class VirtualOutputConfig : OutputConfig
{
    public override OutputKind Kind => OutputKind.Virtual;

    protected override string DefaultName => "Virtual output";

    protected override void ValidateCore(List<ValidationIssue> issues)
    {
    }
}
