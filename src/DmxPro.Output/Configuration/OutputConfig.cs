using System.Text.Json.Serialization;
using DmxPro.Core.Patching;
using DmxPro.Core.Validation;

namespace DmxPro.Output.Configuration;

/// <summary>
/// Persistable settings of one output. There is deliberately no "enabled" flag: enabling output is a runtime action
/// of <see cref="OutputManager"/> and is never saved (safety rule 1).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(VirtualOutputConfig), "virtual")]
[JsonDerivedType(typeof(ArtNetOutputConfig), "artnet")]
[JsonDerivedType(typeof(SacnOutputConfig), "sacn")]
[JsonDerivedType(typeof(EnttecProOutputConfig), "enttecPro")]
[JsonDerivedType(typeof(OpenDmxOutputConfig), "openDmx")]
public abstract class OutputConfig
{
    public const int MinFrameRateHz = 1;

    /// <summary>DMX512 at full 512 slots cannot refresh faster than ~44 Hz; Art-Net also recommends this limit.</summary>
    public const int MaxFrameRateHz = 44;

    public const int DefaultFrameRateHz = 44;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Project universes (UI numbering, from 1) this output transmits.</summary>
    public List<int> Universes { get; set; } = [1];

    /// <summary>Refresh rate: every universe is re-sent this often even if unchanged (also the max rate).</summary>
    public int FrameRateHz { get; set; } = DefaultFrameRateHz;

    [JsonIgnore]
    public abstract OutputKind Kind { get; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();

    /// <summary>
    /// Identifies the physical device for duplicate detection (two outputs on one COM port), or <c>null</c> when
    /// several outputs may share the transport.
    /// </summary>
    [JsonIgnore]
    public virtual string? DeviceKey => null;

    [JsonIgnore]
    protected abstract string DefaultName { get; }

    /// <summary>Checks the settings. Any <see cref="ValidationSeverity.Error"/> blocks enabling output (safety rule 2).</summary>
    public IReadOnlyList<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();
        ValidateCommon(issues);
        ValidateCore(issues);
        return issues;
    }

    /// <summary>Kind-specific checks.</summary>
    protected abstract void ValidateCore(List<ValidationIssue> issues);

    /// <summary>Universes in configured order without duplicates or invalid numbers.</summary>
    internal int[] GetValidUniverses()
    {
        var result = new List<int>();
        foreach (var universe in Universes ?? [])
        {
            if (DmxAddress.IsValidUniverse(universe) && !result.Contains(universe))
            {
                result.Add(universe);
            }
        }

        return [.. result];
    }

    protected ValidationIssue Error(string code, string message) =>
        ValidationIssue.Error(code, $"{DisplayName}: {message}", Id);

    protected ValidationIssue Warning(string code, string message) =>
        ValidationIssue.Warning(code, $"{DisplayName}: {message}", Id);

    private void ValidateCommon(List<ValidationIssue> issues)
    {
        if (Universes is null || Universes.Count == 0)
        {
            issues.Add(Error("OUTPUT_NO_UNIVERSES", "choose at least one universe to send."));
        }
        else
        {
            var seen = new HashSet<int>();
            foreach (var universe in Universes)
            {
                if (!DmxAddress.IsValidUniverse(universe))
                {
                    issues.Add(Error(
                        "OUTPUT_UNIVERSE_RANGE",
                        $"universe {universe} is out of range (1–{DmxAddress.MaxUniverse})."));
                }
                else if (!seen.Add(universe))
                {
                    issues.Add(Error("OUTPUT_UNIVERSE_DUPLICATE", $"universe {universe} is listed twice."));
                }
            }
        }

        if (FrameRateHz is < MinFrameRateHz or > MaxFrameRateHz)
        {
            issues.Add(Error(
                "OUTPUT_FRAME_RATE_RANGE",
                $"frame rate {FrameRateHz} Hz is out of range ({MinFrameRateHz}–{MaxFrameRateHz} Hz)."));
        }
    }
}
