using DmxPro.Core.Profiles;

namespace DmxPro.Core.Tests;

public class FixtureProfileTests
{
    [Fact]
    public void Validate_ValidProfile_ReturnsNoIssues()
    {
        Assert.Empty(TestProfiles.Rgbw().Validate());
    }

    [Fact]
    public void ResolveChannels_ModeOrder_FollowsModeNotDefinitionOrder()
    {
        var profile = TestProfiles.Rgbw();
        var mode = new FixtureMode { Name = "GRB", Channels = ["g", "r", "b"] };

        var channels = profile.ResolveChannels(mode);

        Assert.Equal([ChannelType.Green, ChannelType.Red, ChannelType.Blue], channels.Select(c => c.Type));
    }

    [Fact]
    public void Validate_ModeReferencesUnknownChannel_ReportsError()
    {
        var profile = TestProfiles.Rgbw();
        profile.Modes.Add(new FixtureMode { Name = "bad", Channels = ["r", "nope"] });

        Assert.Contains(profile.Validate(), i => i.Code == "MODE_UNKNOWN_CHANNEL");
    }

    [Fact]
    public void Validate_DuplicateChannelKey_ReportsError()
    {
        var profile = TestProfiles.Rgbw();
        profile.Channels.Add(new ChannelDefinition { Key = "r", Name = "Red again", Type = ChannelType.Red });

        Assert.Contains(profile.Validate(), i => i.Code == "CHANNEL_KEY_DUPLICATE");
    }

    [Fact]
    public void Validate_CapabilityOutsideByteRange_ReportsError()
    {
        var profile = TestProfiles.Rgbw();
        profile.Channels[5].Capabilities.Add(new ChannelCapability { Min = 200, Max = 300, Label = "x" });

        Assert.Contains(profile.Validate(), i => i.Code == "CAPABILITY_RANGE_INVALID");
    }

    [Fact]
    public void Validate_FineWithoutCoarse_ReportsError()
    {
        var profile = TestProfiles.Rgbw();
        profile.Channels.Add(new ChannelDefinition { Key = "fine", Name = "Fine", Type = ChannelType.Fine });

        Assert.Contains(profile.Validate(), i => i.Code == "FINE_WITHOUT_COARSE");
    }

    [Fact]
    public void Validate_NoModes_ReportsError()
    {
        var profile = TestProfiles.Rgbw();
        profile.Modes.Clear();

        Assert.Contains(profile.Validate(), i => i.Code == "PROFILE_NO_MODES");
    }

    [Theory]
    [InlineData(ChannelType.Dimmer, true)]
    [InlineData(ChannelType.Red, true)]
    [InlineData(ChannelType.White, true)]
    [InlineData(ChannelType.Strobe, false)]
    [InlineData(ChannelType.ColorMacro, false)]
    [InlineData(ChannelType.Generic, false)]
    public void IsIntensity_ChannelType_MatchesHtpRule(ChannelType type, bool expected)
    {
        Assert.Equal(expected, type.IsIntensity());
    }
}
