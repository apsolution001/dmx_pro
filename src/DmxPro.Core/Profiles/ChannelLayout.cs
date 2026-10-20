using System.Text;
using DmxPro.Core.Patching;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Profiles;

/// <summary>
/// The channel layout editor's model: turns a list of <see cref="ChannelLayoutRow"/>s (what each DMX channel does, in
/// the fixture's own order) into a single-mode, user-defined, unverified <see cref="FixtureProfile"/> and back.
/// Every light has its own channel order, so nothing here assumes channel 1 is Red; the built-in layouts are only
/// starting templates.
/// </summary>
public static class ChannelLayout
{
    public const int MaxChannels = DmxAddress.UniverseSize;

    public const string DefaultManufacturer = "Custom";

    /// <summary>Written to <see cref="FixtureProfile.Source"/> of every profile built here.</summary>
    public const string EditorSource = "Channel layout editor (user-defined, not verified on the fixture)";

    /// <summary>
    /// Builds a single-mode, unverified, user-defined profile whose only mode is named "{n}ch". With
    /// <paramref name="existing"/> (editing) the id and type are kept and the revision is bumped; otherwise a new
    /// unique id <c>custom-&lt;slug&gt;-&lt;8 hex&gt;</c> is created. Channel keys are generated from the type
    /// (<c>red</c>, <c>red-2</c>, <c>dimmer</c>, …; generic channels use their channel number: <c>generic-5</c>), so
    /// scene values follow a channel's meaning when the layout is reordered.
    /// </summary>
    /// <remarks>
    /// Strobe safety: no strobe ranges are invented for a user layout. Without a documented non-safe range the engine
    /// never drives the channel as hardware strobe, and while strobe is not allowed it holds the channel at its default
    /// (<see cref="DefaultValueFor"/> gives 0, the usual "no strobe" value). Ranges carried over from a template channel
    /// of the same type are kept.
    /// </remarks>
    /// <exception cref="ArgumentException">The rows have errors (see <see cref="Validate"/>); validate first.</exception>
    public static FixtureProfile CreateProfile(
        string manufacturer,
        string model,
        IReadOnlyList<ChannelLayoutRow> rows,
        FixtureProfile? existing = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var errors = Validate(model, rows).Where(i => i.Severity == ValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)), nameof(rows));
        }

        var trimmedModel = model.Trim();
        var profile = new FixtureProfile
        {
            Id = existing?.Id ?? NewId(trimmedModel),
            Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? DefaultManufacturer : manufacturer.Trim(),
            Model = trimmedModel,
            Type = existing?.Type ?? "Color Changer",
            Revision = existing is null ? 1 : existing.Revision + 1,
            Verified = false,
            UserDefined = true,
            Source = EditorSource,
        };

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new Dictionary<ChannelType, int>();
        var mode = new FixtureMode { Name = $"{rows.Count}ch" };
        ChannelDefinition? lastCoarse = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var channelNumber = i + 1;
            var occurrence = occurrences.GetValueOrDefault(row.Type) + 1;
            occurrences[row.Type] = occurrence;

            var key = UniqueKey(
                row.Type == ChannelType.Generic ? $"generic-{channelNumber}" : KeyFor(row.Type, occurrence),
                usedKeys);

            string name;
            if (!string.IsNullOrWhiteSpace(row.Name))
            {
                name = row.Name.Trim();
            }
            else if (row.Type == ChannelType.Fine && lastCoarse is not null)
            {
                name = $"{lastCoarse.Name} fine";
            }
            else
            {
                name = DefaultNameFor(row.Type, row.Type == ChannelType.Generic ? channelNumber : occurrence);
            }

            var channel = new ChannelDefinition
            {
                Key = key,
                Name = name,
                Type = row.Type,
                Default = row.DefaultValue,
                Capabilities = row.Capabilities.Select(c => c.Clone()).ToList(),
            };

            if (row.Type == ChannelType.Fine)
            {
                // Validate guarantees a coarse channel exists before every fine channel.
                channel.CoarseKey = lastCoarse!.Key;
            }
            else
            {
                lastCoarse = channel;
            }

            profile.Channels.Add(channel);
            mode.Channels.Add(key);
        }

        profile.Modes.Add(mode);
        return profile;
    }

    /// <summary>
    /// Rows for one mode of any profile, in DMX order (editing a custom layout, or using a built-in profile as a
    /// template). Names, defaults and documented ranges are copied.
    /// </summary>
    /// <exception cref="ArgumentException">The profile has no such mode.</exception>
    public static List<ChannelLayoutRow> FromMode(FixtureProfile profile, string modeName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var mode = profile.FindMode(modeName)
            ?? throw new ArgumentException($"Profile '{profile.DisplayName}' has no mode '{modeName}'.", nameof(modeName));

        var rows = new List<ChannelLayoutRow>(mode.ChannelCount);
        foreach (var channel in profile.ResolveChannels(mode))
        {
            rows.Add(new ChannelLayoutRow
            {
                Type = channel.Type,
                Name = channel.Name,
                DefaultValue = channel.Default,
                Capabilities = channel.Capabilities.Select(c => c.Clone()).ToList(),
            });
        }

        return rows;
    }

    /// <summary>
    /// Checks a layout before it is saved. Errors: model name missing, fewer than 1 or more than
    /// <see cref="MaxChannels"/> channels, a fine channel with no coarse channel before it. Warnings: a fine channel not
    /// directly after its coarse channel, the same meaning used twice, no channel meaning set at all.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(string model, IReadOnlyList<ChannelLayoutRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model))
        {
            issues.Add(ValidationIssue.Error("LAYOUT_MODEL_MISSING", "Enter a name for the layout, e.g. \"My PAR 7ch\"."));
        }

        if (rows.Count == 0)
        {
            issues.Add(ValidationIssue.Error("LAYOUT_NO_CHANNELS", "The layout needs at least one channel."));
            return issues;
        }

        if (rows.Count > MaxChannels)
        {
            issues.Add(ValidationIssue.Error(
                "LAYOUT_TOO_MANY_CHANNELS",
                $"The layout has {rows.Count} channels; a DMX universe has {MaxChannels}."));
        }

        var seenCoarse = false;
        var counts = new Dictionary<ChannelType, int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var type = rows[i].Type;
            counts[type] = counts.GetValueOrDefault(type) + 1;

            if (type != ChannelType.Fine)
            {
                seenCoarse = true;
                continue;
            }

            if (!seenCoarse)
            {
                issues.Add(ValidationIssue.Error(
                    "LAYOUT_FINE_WITHOUT_COARSE",
                    $"Channel {i + 1} is a fine channel, but there is no coarse channel before it. Put it right after the channel it refines."));
            }
            else if (rows[i - 1].Type == ChannelType.Fine)
            {
                issues.Add(ValidationIssue.Warning(
                    "LAYOUT_FINE_NOT_AFTER_COARSE",
                    $"Channel {i + 1} is a fine channel but does not directly follow a coarse channel; it is paired with the nearest coarse channel before it."));
            }
        }

        foreach (var (type, count) in counts)
        {
            if (count > 1 && type is not (ChannelType.Generic or ChannelType.Fine))
            {
                issues.Add(ValidationIssue.Warning(
                    "LAYOUT_DUPLICATE_TYPE",
                    $"{DefaultNameFor(type, 1)} is used on {count} channels. That is fine if the light really has several, otherwise check the layout."));
            }
        }

        if (counts.Count == 1 && counts.ContainsKey(ChannelType.Generic))
        {
            issues.Add(ValidationIssue.Warning(
                "LAYOUT_NO_MEANING",
                "No channel meaning is set: colours, dimmer and effects cannot drive this light, only raw channel values."));
        }

        return issues;
    }

    /// <summary>
    /// Default channel name: "Red", "Red 2", "Color macro". For <see cref="ChannelType.Generic"/> pass the 1-based
    /// channel number: "Channel 5".
    /// </summary>
    public static string DefaultNameFor(ChannelType type, int occurrence)
    {
        if (type == ChannelType.Generic)
        {
            return $"Channel {occurrence}";
        }

        var name = type switch
        {
            ChannelType.ColorMacro => "Color macro",
            ChannelType.UV => "UV",
            _ => type.ToString(),
        };
        return occurrence > 1 ? $"{name} {occurrence}" : name;
    }

    /// <summary>
    /// Default value for a new channel of <paramref name="type"/>: Dimmer 255 (so colours show at full level), everything
    /// else 0 — for Strobe that is the usual "no strobe" value, for Color macro / Program "manual / off".
    /// </summary>
    public static byte DefaultValueFor(ChannelType type) => type == ChannelType.Dimmer ? (byte)255 : (byte)0;

    /// <summary><paramref name="channelCount"/> generic channels ("Channel 1", "Channel 2", …), clamped to 1–512.</summary>
    public static List<ChannelLayoutRow> Empty(int channelCount)
    {
        var count = Math.Clamp(channelCount, 1, MaxChannels);
        var rows = new List<ChannelLayoutRow>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new ChannelLayoutRow());
        }

        return rows;
    }

    /// <summary>
    /// Built-in starting templates (fresh rows on every call, safe to edit). They are common layouts, not a promise
    /// about any particular light — the user adjusts them to the fixture's DMX chart.
    /// </summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<ChannelLayoutRow> Rows)> Templates =>
    [
        ("Empty (1 channel)", Empty(1)),
        ("Dimmer", Rows(ChannelType.Dimmer)),
        ("RGB", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue)),
        ("RGBW", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White)),
        ("RGBA", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.Amber)),
        ("RGBWA", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber)),
        ("RGBWA+UV", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV)),
        ("Dimmer + RGB", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue)),
        ("Dimmer + RGBW", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White)),
        ("Dimmer + RGB + Strobe", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.Strobe)),
        ("Dimmer + RGBW + Strobe", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Strobe)),
    ];

    /// <summary>True for profiles created with the layout editor (editable); built-in/imported profiles are read-only templates.</summary>
    public static bool IsCustom(FixtureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.UserDefined;
    }

    private static List<ChannelLayoutRow> Rows(params ChannelType[] types)
    {
        var rows = new List<ChannelLayoutRow>(types.Length);
        foreach (var type in types)
        {
            rows.Add(new ChannelLayoutRow { Type = type, DefaultValue = DefaultValueFor(type) });
        }

        return rows;
    }

    private static string KeyFor(ChannelType type, int occurrence)
    {
        var baseKey = type switch
        {
            ChannelType.ColorMacro => "color-macro",
            _ => type.ToString().ToLowerInvariant(),
        };
        return occurrence > 1 ? $"{baseKey}-{occurrence}" : baseKey;
    }

    private static string UniqueKey(string key, HashSet<string> used)
    {
        var candidate = key;
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = $"{key}-{n}";
        }

        return candidate;
    }

    private static string NewId(string model) => $"custom-{Slug(model)}-{Guid.NewGuid().ToString("N")[..8]}";

    private static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length >= 32)
            {
                break;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "layout" : slug;
    }
}
