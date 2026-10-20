using DmxPro.Core.Profiles;

namespace DmxPro.Core.Tests;

/// <summary>Bug hunt: copy/paste of light values between different profiles.</summary>
public class BugHuntCopyPasteTests
{
    private static ChannelDefinition Ch(string key, ChannelType type, string? coarseKey = null) =>
        new() { Key = key, Name = key, Type = type, CoarseKey = coarseKey };

    [Fact]
    public void MatchTo_OtherProfileFineChannels_FineValueGoesToFineOfSameCoarseType()
    {
        // Source: dimmer + dimmer fine, red + red fine. Target: red + red fine only (no dimmer).
        ChannelDefinition[] source =
        [
            Ch("dim", ChannelType.Dimmer), Ch("dim-fine", ChannelType.Fine, "dim"),
            Ch("red", ChannelType.Red), Ch("red-fine", ChannelType.Fine, "red"),
        ];
        ChannelDefinition[] target = [Ch("r", ChannelType.Red), Ch("r-fine", ChannelType.Fine, "r")];
        var copied = CopiedLightValues.Create("profile-a", "Source", source.Zip(new byte[] { 200, 11, 100, 22 }));

        var matches = copied.MatchTo("profile-b", target);

        // The red fine byte (22) belongs on the red fine channel, not the dimmer fine byte (11).
        Assert.Equal([(0, (byte)100), (1, (byte)22)], matches);
    }

    [Fact]
    public void MatchTo_OtherProfilePartialCopyOfSecondWhite_GoesToSecondWhite()
    {
        // A scene that includes only the second (e.g. cold) white of an RGBWW light copies just that channel.
        // The whole source mode is passed with an include flag: from the lone channel alone nobody can tell it was the
        // second White, so the scene editor hands over the full mode list.
        ChannelDefinition[] source =
        [
            Ch("red", ChannelType.Red), Ch("green", ChannelType.Green), Ch("blue", ChannelType.Blue),
            Ch("white-1", ChannelType.White), Ch("white-2", ChannelType.White),
        ];
        var copied = CopiedLightValues.CreateFromMode(
            "rgbww-a",
            "Source",
            source.Select(c => (c, c.Key == "white-2" ? (byte)180 : (byte)0, c.Key == "white-2")));
        ChannelDefinition[] target =
        [
            Ch("r", ChannelType.Red), Ch("g", ChannelType.Green), Ch("b", ChannelType.Blue),
            Ch("w1", ChannelType.White), Ch("w2", ChannelType.White),
        ];

        var matches = copied.MatchTo("rgbww-b", target);

        // "The second White to the second White": index 4, not the first white at index 3.
        Assert.Equal([(4, (byte)180)], matches);
    }

    [Fact]
    public void MatchTo_EmptyCopy_MatchesNothing()
    {
        var copied = CopiedLightValues.Create("a", "Source", []);

        Assert.Empty(copied.MatchTo("a", [Ch("r", ChannelType.Red)]));
        Assert.Empty(copied.MatchTo("b", [Ch("r", ChannelType.Red)]));
    }

    [Fact]
    public void MatchTo_SameProfileDuplicatedKeyInTargetMode_BothGetTheValue()
    {
        var red = Ch("r", ChannelType.Red);
        var copied = CopiedLightValues.Create("a", "Source", [(red, (byte)77)]);

        var matches = copied.MatchTo("A", [red, Ch("g", ChannelType.Green), red]);

        Assert.Equal([(0, (byte)77), (2, (byte)77)], matches);
    }
}
