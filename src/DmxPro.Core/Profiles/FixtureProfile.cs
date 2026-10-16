using DmxPro.Core.Patching;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Profiles;

/// <summary>
/// Definition of a fixture model: its channels and DMX modes. This is the single source of truth for what each
/// channel means — nothing in the app may assume "Red = channel 1".
/// </summary>
public sealed class FixtureProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Stable id, e.g. <c>generic-rgbw-par</c>. Fixtures reference profiles by this id.</summary>
    public string Id { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Free-form category such as "Color Changer" or "LED Bar".</summary>
    public string Type { get; set; } = "Color Changer";

    /// <summary>Profile revision; bump when channels change so projects can detect stale copies.</summary>
    public int Revision { get; set; } = 1;

    /// <summary>True only after the channel map was confirmed on the real fixture.</summary>
    public bool Verified { get; set; }

    /// <summary>Where the channel map came from (manual page, QLC+ library, user-defined…).</summary>
    public string? Source { get; set; }

    public List<ChannelDefinition> Channels { get; set; } = [];

    public List<FixtureMode> Modes { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Manufacturer) ? Model : $"{Manufacturer} {Model}";

    public FixtureMode? FindMode(string? modeName) =>
        Modes.FirstOrDefault(m => string.Equals(m.Name, modeName, StringComparison.OrdinalIgnoreCase));

    public ChannelDefinition? FindChannel(string key) =>
        Channels.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));

    /// <summary>Channel definitions of a mode in DMX order. Unknown keys are skipped (see <see cref="Validate"/>).</summary>
    public IReadOnlyList<ChannelDefinition> ResolveChannels(FixtureMode mode)
    {
        var result = new List<ChannelDefinition>(mode.Channels.Count);
        foreach (var key in mode.Channels)
        {
            if (FindChannel(key) is { } channel)
            {
                result.Add(channel);
            }
        }

        return result;
    }

    /// <summary>Checks internal consistency. An empty result means the profile is usable.</summary>
    public IReadOnlyList<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();
        var name = string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;

        if (string.IsNullOrWhiteSpace(Id))
        {
            issues.Add(ValidationIssue.Error("PROFILE_ID_MISSING", "Profile has no id."));
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            issues.Add(ValidationIssue.Error("PROFILE_MODEL_MISSING", $"Profile '{Id}' has no model name."));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in Channels)
        {
            if (string.IsNullOrWhiteSpace(channel.Key))
            {
                issues.Add(ValidationIssue.Error("CHANNEL_KEY_MISSING", $"{name}: channel '{channel.Name}' has no key."));
            }
            else if (!keys.Add(channel.Key))
            {
                issues.Add(ValidationIssue.Error("CHANNEL_KEY_DUPLICATE", $"{name}: channel key '{channel.Key}' is used twice."));
            }

            ValidateCapabilities(channel, name, issues);
        }

        foreach (var channel in Channels.Where(c => c.Type == ChannelType.Fine))
        {
            if (channel.CoarseKey is null || !keys.Contains(channel.CoarseKey))
            {
                issues.Add(ValidationIssue.Error(
                    "FINE_WITHOUT_COARSE",
                    $"{name}: fine channel '{channel.Name}' must name an existing coarse channel."));
            }
        }

        if (Modes.Count == 0)
        {
            issues.Add(ValidationIssue.Error("PROFILE_NO_MODES", $"{name}: profile has no DMX modes."));
        }

        var modeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in Modes)
        {
            if (string.IsNullOrWhiteSpace(mode.Name) || !modeNames.Add(mode.Name))
            {
                issues.Add(ValidationIssue.Error("MODE_NAME_INVALID", $"{name}: mode names must be unique and not empty."));
            }

            if (mode.Channels.Count == 0)
            {
                issues.Add(ValidationIssue.Error("MODE_EMPTY", $"{name}: mode '{mode.Name}' has no channels."));
            }

            if (mode.Channels.Count > DmxAddress.UniverseSize)
            {
                issues.Add(ValidationIssue.Error(
                    "MODE_TOO_LARGE",
                    $"{name}: mode '{mode.Name}' uses {mode.Channels.Count} channels; a universe has {DmxAddress.UniverseSize}."));
            }

            foreach (var key in mode.Channels.Where(k => !keys.Contains(k)).Distinct())
            {
                issues.Add(ValidationIssue.Error(
                    "MODE_UNKNOWN_CHANNEL",
                    $"{name}: mode '{mode.Name}' references unknown channel '{key}'."));
            }
        }

        return issues;
    }

    private static void ValidateCapabilities(ChannelDefinition channel, string profileName, List<ValidationIssue> issues)
    {
        var ordered = channel.Capabilities.OrderBy(c => c.Min).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var cap = ordered[i];
            if (cap.Min < 0 || cap.Max > 255 || cap.Min > cap.Max)
            {
                issues.Add(ValidationIssue.Error(
                    "CAPABILITY_RANGE_INVALID",
                    $"{profileName}: channel '{channel.Name}' range {cap.Min}–{cap.Max} is invalid (must be within 0–255)."));
            }
            else if (i > 0 && cap.Min <= ordered[i - 1].Max)
            {
                issues.Add(ValidationIssue.Warning(
                    "CAPABILITY_RANGE_OVERLAP",
                    $"{profileName}: channel '{channel.Name}' ranges {ordered[i - 1].Min}–{ordered[i - 1].Max} and {cap.Min}–{cap.Max} overlap."));
            }
        }
    }

    public FixtureProfile Clone()
    {
        var copy = (FixtureProfile)MemberwiseClone();
        copy.Channels = Channels.Select(c => c.Clone()).ToList();
        copy.Modes = Modes.Select(m => m.Clone()).ToList();
        return copy;
    }
}
