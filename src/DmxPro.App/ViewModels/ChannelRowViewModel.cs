using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>One row of a profile's channel table.</summary>
public sealed class ChannelRowViewModel
{
    public ChannelRowViewModel(int number, ChannelDefinition channel)
    {
        Number = number;
        Name = channel.Name;
        Type = channel.Type == ChannelType.Fine && channel.CoarseKey is not null ? $"Fine ({channel.CoarseKey})" : channel.Type.ToString();
        Default = channel.Default;
        Ranges = channel.Capabilities.Count == 0
            ? channel.Description ?? "0–255"
            : string.Join("  ·  ", channel.Capabilities.Select(c => $"{c.Min}–{c.Max} {c.Label}"));
        IsGeneric = channel.Type == ChannelType.Generic;
    }

    public int Number { get; }

    public string Name { get; }

    public string Type { get; }

    public byte Default { get; }

    public string Ranges { get; }

    /// <summary>Unknown meaning: shown as raw channel with a warning badge.</summary>
    public bool IsGeneric { get; }
}
