using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>"Start from" entry of the channel layout editor (a bindable copy of one <see cref="ChannelLayout.Templates"/> item).</summary>
public sealed record ChannelLayoutTemplate(string Name, IReadOnlyList<ChannelLayoutRow> Rows)
{
    /// <summary>"RGBW (4 ch)"; names that already state their size are shown as they are.</summary>
    public string Display => Name.Contains('(', StringComparison.Ordinal) ? Name : $"{Name} ({Rows.Count} ch)";
}
