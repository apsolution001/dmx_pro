using DmxPro.Core.Profiles;

namespace DmxPro.Core.Tests;

internal static class TestProfiles
{
    public static FixtureProfile Rgbw() => new()
    {
        Id = "test-rgbw",
        Manufacturer = "Test",
        Model = "RGBW",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
            new() { Key = "w", Name = "White", Type = ChannelType.White },
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer, Default = 255 },
            new() { Key = "strobe", Name = "Strobe", Type = ChannelType.Strobe },
            new() { Key = "macro", Name = "Macro", Type = ChannelType.ColorMacro },
            new() { Key = "speed", Name = "Speed", Type = ChannelType.Speed },
        ],
        Modes =
        [
            new() { Name = "4ch", Channels = ["r", "g", "b", "w"] },
            new() { Name = "8ch", Channels = ["r", "g", "b", "w", "dim", "strobe", "macro", "speed"] },
        ],
    };
}
