using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Profiles;

/// <summary>
/// Imports QLC+ fixture definitions (<c>.qxf</c>, XML). The QLC+ fixture library is Apache-2.0 licensed; the
/// imported profile records its origin in <see cref="FixtureProfile.Source"/>. Imported profiles are always
/// marked unverified, and channels we cannot classify are imported as <see cref="ChannelType.Generic"/>.
/// </summary>
public static class QxfImporter
{
    public const string FileExtension = ".qxf";

    public static FixtureProfile ImportFile(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (XmlException ex)
        {
            throw new DmxFileFormatException($"Not a valid QLC+ fixture file: {ex.Message}", ex);
        }

        return Import(document, Path.GetFileName(path));
    }

    public static FixtureProfile Import(XDocument document, string? sourceName = null)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "FixtureDefinition")
        {
            throw new DmxFileFormatException("Not a QLC+ fixture definition (missing <FixtureDefinition>).");
        }

        var manufacturer = ChildValue(root, "Manufacturer");
        var model = ChildValue(root, "Model");
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DmxFileFormatException("QLC+ fixture definition has no <Model>.");
        }

        var author = Child(root, "Creator") is { } creator ? ChildValue(creator, "Author") : string.Empty;
        var profile = new FixtureProfile
        {
            Id = Slug($"{manufacturer}-{model}"),
            Manufacturer = manufacturer,
            Model = model,
            Type = ChildValue(root, "Type") is { Length: > 0 } type ? type : "Other",
            Verified = false,
            Source = $"Imported from QLC+ fixture definition{(sourceName is null ? string.Empty : $" '{sourceName}'")} " +
                     $"(QLC+ fixture library, Apache-2.0){(author.Length > 0 ? $", author: {author}" : string.Empty)}.",
        };

        // QLC+ modes reference channels by name; map each name to our unique key.
        var keyByName = new Dictionary<string, string>(StringComparer.Ordinal);
        var presetByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in Children(root, "Channel"))
        {
            var name = (string?)element.Attribute("Name") ?? string.Empty;
            var key = UniqueKey(Slug(name), profile.Channels);
            keyByName.TryAdd(name, key);

            var preset = (string?)element.Attribute("Preset") ?? string.Empty;
            presetByKey[key] = preset;

            var channel = new ChannelDefinition
            {
                Key = key,
                Name = name,
                Type = Classify(element, preset),
                Default = ParseByte((string?)element.Attribute("Default")),
                Capabilities = Children(element, "Capability").Select(ToCapability).ToList(),
            };
            if (channel.Type == ChannelType.Strobe)
            {
                MarkSafeStrobeRanges(channel);
            }
            if (channel.Type == ChannelType.Generic && DescribeUnsupported(element, preset) is { } note)
            {
                channel.Description = note;
            }

            profile.Channels.Add(channel);
        }

        LinkFineChannels(profile, presetByKey);

        foreach (var element in Children(root, "Mode"))
        {
            var mode = new FixtureMode { Name = (string?)element.Attribute("Name") ?? string.Empty };
            foreach (var channelRef in Children(element, "Channel")
                         .OrderBy(c => ParseInt((string?)c.Attribute("Number"))))
            {
                if (keyByName.TryGetValue(channelRef.Value.Trim(), out var key))
                {
                    mode.Channels.Add(key);
                }
                else
                {
                    throw new DmxFileFormatException(
                        $"Mode '{mode.Name}' references unknown channel '{channelRef.Value.Trim()}'.");
                }
            }

            profile.Modes.Add(mode);
        }

        return profile;
    }

    private static ChannelType Classify(XElement channel, string preset)
    {
        if (preset.Length > 0)
        {
            return ClassifyPreset(preset);
        }

        var group = Child(channel, "Group");
        var groupName = group?.Value.Trim() ?? string.Empty;
        if (group is not null && (string?)group.Attribute("Byte") == "1")
        {
            return ChannelType.Fine;
        }

        return groupName switch
        {
            "Intensity" => ChildValue(channel, "Colour") switch
            {
                "Red" => ChannelType.Red,
                "Green" => ChannelType.Green,
                "Blue" => ChannelType.Blue,
                "White" => ChannelType.White,
                "Amber" => ChannelType.Amber,
                "UV" => ChannelType.UV,
                "" => ChannelType.Dimmer,
                _ => ChannelType.Generic,
            },
            "Shutter" => ChannelType.Strobe,
            "Speed" => ChannelType.Speed,
            "Colour" => ChannelType.ColorMacro,
            "Effect" => ChannelType.Program,
            _ => ChannelType.Generic,
        };
    }

    private static ChannelType ClassifyPreset(string preset)
    {
        if (preset.EndsWith("Fine", StringComparison.Ordinal))
        {
            return ChannelType.Fine;
        }

        return preset switch
        {
            "IntensityMasterDimmer" or "IntensityDimmer" => ChannelType.Dimmer,
            "IntensityRed" => ChannelType.Red,
            "IntensityGreen" => ChannelType.Green,
            "IntensityBlue" => ChannelType.Blue,
            "IntensityWhite" => ChannelType.White,
            "IntensityAmber" => ChannelType.Amber,
            "IntensityUV" => ChannelType.UV,
            _ when preset.StartsWith("ShutterStrobe", StringComparison.Ordinal) => ChannelType.Strobe,
            _ when preset.StartsWith("Speed", StringComparison.Ordinal) => ChannelType.Speed,
            "ColorMacro" or "ColorWheel" => ChannelType.ColorMacro,
            _ => ChannelType.Generic,
        };
    }

    private static string? DescribeUnsupported(XElement channel, string preset)
    {
        var group = Child(channel, "Group")?.Value.Trim();
        var origin = preset.Length > 0 ? preset : group;
        return string.IsNullOrEmpty(origin) || origin == "Nothing"
            ? null
            : $"QLC+ type '{origin}' — not a supported control type; available as a raw channel.";
    }

    /// <summary>Points each fine channel at its coarse partner (same preset without "Fine", or same name without "Fine").</summary>
    private static void LinkFineChannels(FixtureProfile profile, Dictionary<string, string> presetByKey)
    {
        foreach (var fine in profile.Channels.Where(c => c.Type == ChannelType.Fine))
        {
            var finePreset = presetByKey[fine.Key];
            var coarse = profile.Channels.FirstOrDefault(c =>
                c.Type != ChannelType.Fine &&
                ((finePreset.Length > 4 && presetByKey[c.Key] == finePreset[..^4]) ||
                 string.Equals(c.Name, StripFine(fine.Name), StringComparison.OrdinalIgnoreCase)));

            if (coarse is null)
            {
                // Without a partner we cannot know what this channel refines; never guess.
                fine.Type = ChannelType.Generic;
                fine.Description = "Fine (16-bit) channel without an identifiable coarse channel.";
            }
            else
            {
                fine.CoarseKey = coarse.Key;
            }
        }
    }

    private static string StripFine(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith("fine", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4].TrimEnd(' ', '-', '_') : trimmed;
    }

    private static ChannelCapability ToCapability(XElement element) => new()
    {
        Min = ParseInt((string?)element.Attribute("Min")),
        Max = ParseInt((string?)element.Attribute("Max"), 255),
        Label = element.Value.Trim(),
        Preset = (string?)element.Attribute("Preset") is { Length: > 0 } preset ? preset : null,
    };

    /// <summary>
    /// QLC+ has no "safe" flag: mark the ranges the shared classifier recognises as steady (shutter open, no function,
    /// no strobe…) so the engine's strobe safety rests the channel there instead of at a strobing value.
    /// </summary>
    private static void MarkSafeStrobeRanges(ChannelDefinition channel)
    {
        foreach (var capability in channel.Capabilities)
        {
            if (StrobeCapabilityClassifier.Classify(capability) == StrobeRangeKind.Steady)
            {
                capability.Safe = true;
            }
        }
    }

    private static string UniqueKey(string baseKey, List<ChannelDefinition> existing)
    {
        var key = baseKey.Length == 0 ? "channel" : baseKey;
        var candidate = key;
        for (var n = 2; existing.Any(c => c.Key == candidate); n++)
        {
            candidate = $"{key}-{n}";
        }

        return candidate;
    }

    /// <summary>Lower-case, ASCII letters/digits separated by single dashes.</summary>
    internal static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private static int ParseInt(string? value, int fallback = 0) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;

    private static byte ParseByte(string? value) => (byte)Math.Clamp(ParseInt(value), 0, 255);

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> Children(XElement parent, string localName) =>
        parent.Elements().Where(e => e.Name.LocalName == localName);

    private static string ChildValue(XElement parent, string localName) => Child(parent, localName)?.Value.Trim() ?? string.Empty;
}
