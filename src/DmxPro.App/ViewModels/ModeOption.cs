using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>DMX mode choice in the Add Fixture dialog, shown as "8ch Full (8 channels)".</summary>
public sealed record ModeOption(FixtureMode Mode)
{
    public string Name => Mode.Name;

    public int ChannelCount => Mode.ChannelCount;

    public string Display => ChannelCount == 1 ? $"{Name} (1 channel)" : $"{Name} ({ChannelCount} channels)";

    public override string ToString() => Display;
}
