using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Tests;

public class ChannelLayoutTests
{
    private static List<ChannelLayoutRow> Rows(params ChannelType[] types) =>
        types.Select(t => new ChannelLayoutRow { Type = t, DefaultValue = ChannelLayout.DefaultValueFor(t) }).ToList();

    [Fact]
    public void CreateProfile_Rows_SingleUnverifiedUserDefinedModeInRowOrder()
    {
        var profile = ChannelLayout.CreateProfile(
            "",
            " My PAR 7ch ",
            Rows(ChannelType.Green, ChannelType.Dimmer, ChannelType.Red, ChannelType.Blue, ChannelType.Generic, ChannelType.Strobe, ChannelType.ColorMacro));

        Assert.Equal("Custom", profile.Manufacturer);
        Assert.Equal("My PAR 7ch", profile.Model);
        Assert.False(profile.Verified);
        Assert.True(profile.UserDefined);
        Assert.True(ChannelLayout.IsCustom(profile));
        Assert.Equal(1, profile.Revision);
        Assert.StartsWith("custom-my-par-7ch-", profile.Id, StringComparison.Ordinal);
        var mode = Assert.Single(profile.Modes);
        Assert.Equal("7ch", mode.Name);
        Assert.Equal(["green", "dimmer", "red", "blue", "generic-5", "strobe", "color-macro"], mode.Channels);
        Assert.Equal(
            [ChannelType.Green, ChannelType.Dimmer, ChannelType.Red, ChannelType.Blue, ChannelType.Generic, ChannelType.Strobe, ChannelType.ColorMacro],
            profile.ResolveChannels(mode).Select(c => c.Type));
        Assert.Equal(["Green", "Dimmer", "Red", "Blue", "Channel 5", "Strobe", "Color macro"], profile.Channels.Select(c => c.Name));
        Assert.Equal(255, profile.FindChannel("dimmer")!.Default);
        Assert.Equal(0, profile.FindChannel("strobe")!.Default);
        Assert.Empty(profile.FindChannel("strobe")!.Capabilities);
        Assert.Empty(profile.Validate());
    }

    [Fact]
    public void CreateProfile_DuplicateTypes_KeysAndNamesUnique()
    {
        var profile = ChannelLayout.CreateProfile("X", "Twin", Rows(ChannelType.Red, ChannelType.Red, ChannelType.Generic, ChannelType.Generic));

        Assert.Equal(["red", "red-2", "generic-3", "generic-4"], profile.Modes[0].Channels);
        Assert.Equal(["Red", "Red 2", "Channel 3", "Channel 4"], profile.Channels.Select(c => c.Name));
        Assert.Empty(profile.Validate());
    }

    [Fact]
    public void CreateProfile_CustomNameAndDefault_Kept()
    {
        var rows = new List<ChannelLayoutRow>
        {
            new() { Type = ChannelType.Program, Name = "  Auto show ", DefaultValue = 7 },
        };

        var channel = Assert.Single(ChannelLayout.CreateProfile("X", "P", rows).Channels);

        Assert.Equal("Auto show", channel.Name);
        Assert.Equal(7, channel.Default);
    }

    [Fact]
    public void CreateProfile_FineChannel_PairedWithPreviousCoarse()
    {
        var profile = ChannelLayout.CreateProfile("X", "16bit", Rows(ChannelType.Dimmer, ChannelType.Fine));

        var fine = profile.FindChannel("fine")!;
        Assert.Equal("dimmer", fine.CoarseKey);
        Assert.Equal("Dimmer fine", fine.Name);
        Assert.Empty(profile.Validate());
    }

    [Fact]
    public void CreateProfile_InvalidRows_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChannelLayout.CreateProfile("X", "", Rows(ChannelType.Red)));
        Assert.Throws<ArgumentException>(() => ChannelLayout.CreateProfile("X", "M", []));
    }

    [Fact]
    public void CreateProfile_TwoNewProfiles_GetDifferentIds()
    {
        var a = ChannelLayout.CreateProfile("X", "Same", Rows(ChannelType.Red));
        var b = ChannelLayout.CreateProfile("X", "Same", Rows(ChannelType.Red));

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void CreateProfile_ModelWithoutLettersOrDigits_IdStillValid()
    {
        var profile = ChannelLayout.CreateProfile("X", "!!!", Rows(ChannelType.Red));

        Assert.StartsWith("custom-layout-", profile.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProfile_Edit_KeepsIdAndBumpsRevision()
    {
        var original = ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue));
        original.Type = "LED Bar";

        var edited = ChannelLayout.CreateProfile("Y", "PAR v2", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue), original);

        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(2, edited.Revision);
        Assert.Equal("LED Bar", edited.Type);
        Assert.Equal("Y", edited.Manufacturer);
        Assert.Equal("PAR v2", edited.Model);
        Assert.Equal("4ch", edited.Modes[0].Name);
        Assert.False(edited.Verified);
        Assert.True(edited.UserDefined);
    }

    [Fact]
    public void CreateProfile_EditOfVerifiedProfile_BecomesUnverified()
    {
        var original = ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red));
        original.Verified = true;

        Assert.False(ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red), original).Verified);
    }

    [Fact]
    public void FromMode_CustomProfile_RoundTripsRows()
    {
        var rows = Rows(ChannelType.Green, ChannelType.Dimmer, ChannelType.Red, ChannelType.Fine, ChannelType.Generic);
        rows[4].Name = "Mic sensitivity";
        rows[4].DefaultValue = 42;
        var profile = ChannelLayout.CreateProfile("X", "Odd", rows);

        var back = ChannelLayout.FromMode(profile, profile.Modes[0].Name);

        Assert.Equal(rows.Select(r => r.Type), back.Select(r => r.Type));
        Assert.Equal(rows.Select(r => r.DefaultValue), back.Select(r => r.DefaultValue));
        Assert.Equal("Mic sensitivity", back[4].Name);
        var again = ChannelLayout.CreateProfile("X", "Odd", back, profile);
        Assert.Equal(profile.Modes[0].Channels, again.Modes[0].Channels);
        Assert.Equal(profile.Channels.Select(c => c.Name), again.Channels.Select(c => c.Name));
    }

    [Fact]
    public void FromMode_BuiltInTemplateWithStrobeRanges_KeepsRanges()
    {
        var builtIn = new FixtureProfile
        {
            Id = "t",
            Model = "T",
            Channels =
            [
                new()
                {
                    Key = "s",
                    Name = "Strobe",
                    Type = ChannelType.Strobe,
                    Capabilities = [new() { Min = 0, Max = 9, Label = "No strobe", Safe = true }],
                },
                new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            ],
            Modes = [new() { Name = "2ch", Channels = ["r", "s"] }],
        };

        var rows = ChannelLayout.FromMode(builtIn, "2ch");
        var profile = ChannelLayout.CreateProfile("X", "Copy", rows);

        Assert.Equal([ChannelType.Red, ChannelType.Strobe], rows.Select(r => r.Type));
        Assert.Single(profile.FindChannel("strobe")!.Capabilities);
        Assert.False(ChannelLayout.IsCustom(builtIn));
    }

    [Fact]
    public void FromMode_UnknownMode_Throws()
    {
        var profile = ChannelLayout.CreateProfile("X", "P", Rows(ChannelType.Red));

        Assert.Throws<ArgumentException>(() => ChannelLayout.FromMode(profile, "nope"));
    }

    [Fact]
    public void Type_Changed_DropsCarriedRanges()
    {
        var row = new ChannelLayoutRow { Type = ChannelType.Strobe, Capabilities = [new() { Min = 0, Max = 9, Safe = true }] };

        row.Type = ChannelType.Red;

        Assert.Empty(row.Capabilities);
    }

    [Fact]
    public void Validate_GoodLayout_NoIssues()
    {
        Assert.Empty(ChannelLayout.Validate("P", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NoModelName_Error(string model)
    {
        var issue = Assert.Single(ChannelLayout.Validate(model, Rows(ChannelType.Red)));

        Assert.Equal(("LAYOUT_MODEL_MISSING", ValidationSeverity.Error), (issue.Code, issue.Severity));
    }

    [Fact]
    public void Validate_NoChannels_Error()
    {
        Assert.Contains(ChannelLayout.Validate("P", []), i => i.Code == "LAYOUT_NO_CHANNELS" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_513Channels_Error()
    {
        var rows = ChannelLayout.Empty(512);
        rows.Add(new ChannelLayoutRow { Type = ChannelType.Red });

        Assert.Contains(ChannelLayout.Validate("P", rows), i => i.Code == "LAYOUT_TOO_MANY_CHANNELS" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_512Channels_NoError()
    {
        var rows = ChannelLayout.Empty(511);
        rows.Add(new ChannelLayoutRow { Type = ChannelType.Dimmer });

        Assert.DoesNotContain(ChannelLayout.Validate("P", rows), i => i.Severity == ValidationSeverity.Error);
        Assert.Equal(512, ChannelLayout.CreateProfile("X", "P", rows).Modes[0].ChannelCount);
    }

    [Fact]
    public void Validate_FineFirst_Error()
    {
        var issues = ChannelLayout.Validate("P", Rows(ChannelType.Fine, ChannelType.Red));

        Assert.Contains(issues, i => i.Code == "LAYOUT_FINE_WITHOUT_COARSE" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_FineAfterFine_WarningAndPairedWithNearestCoarse()
    {
        var rows = Rows(ChannelType.Dimmer, ChannelType.Fine, ChannelType.Fine);

        var issue = Assert.Single(ChannelLayout.Validate("P", rows));
        var profile = ChannelLayout.CreateProfile("X", "P", rows);

        Assert.Equal(("LAYOUT_FINE_NOT_AFTER_COARSE", ValidationSeverity.Warning), (issue.Code, issue.Severity));
        Assert.Equal("dimmer", profile.FindChannel("fine-2")!.CoarseKey);
        Assert.Empty(profile.Validate());
    }

    [Fact]
    public void Validate_DuplicateRed_Warning()
    {
        var issue = Assert.Single(ChannelLayout.Validate("P", Rows(ChannelType.Red, ChannelType.Red)));

        Assert.Equal(("LAYOUT_DUPLICATE_TYPE", ValidationSeverity.Warning), (issue.Code, issue.Severity));
    }

    [Fact]
    public void Validate_AllGeneric_NoMeaningWarning()
    {
        var issue = Assert.Single(ChannelLayout.Validate("P", ChannelLayout.Empty(3)));

        Assert.Equal(("LAYOUT_NO_MEANING", ValidationSeverity.Warning), (issue.Code, issue.Severity));
    }

    [Theory]
    [InlineData(ChannelType.Red, 1, "Red")]
    [InlineData(ChannelType.Red, 2, "Red 2")]
    [InlineData(ChannelType.Generic, 5, "Channel 5")]
    [InlineData(ChannelType.ColorMacro, 1, "Color macro")]
    [InlineData(ChannelType.UV, 1, "UV")]
    public void DefaultNameFor_TypeAndOccurrence_ReadableName(ChannelType type, int occurrence, string expected)
    {
        Assert.Equal(expected, ChannelLayout.DefaultNameFor(type, occurrence));
    }

    [Theory]
    [InlineData(ChannelType.Dimmer, 255)]
    [InlineData(ChannelType.Strobe, 0)]
    [InlineData(ChannelType.Red, 0)]
    [InlineData(ChannelType.Generic, 0)]
    public void DefaultValueFor_Type_SafeDefault(ChannelType type, byte expected)
    {
        Assert.Equal(expected, ChannelLayout.DefaultValueFor(type));
    }

    [Fact]
    public void Templates_All_ValidAndBuildValidProfiles()
    {
        Assert.NotEmpty(ChannelLayout.Templates);
        foreach (var (name, rows) in ChannelLayout.Templates)
        {
            Assert.DoesNotContain(ChannelLayout.Validate(name, rows), i => i.Severity == ValidationSeverity.Error);
            Assert.Empty(ChannelLayout.CreateProfile("X", name, rows).Validate());
        }
    }

    [Fact]
    public void Templates_ReturnFreshRows_EditingOneDoesNotChangeTheNext()
    {
        ChannelLayout.Templates.Single(t => t.Name == "RGB").Rows[0].Type = ChannelType.Generic;

        Assert.Equal(ChannelType.Red, ChannelLayout.Templates.Single(t => t.Name == "RGB").Rows[0].Type);
    }

    [Fact]
    public void Templates_Rgbw_RedGreenBlueWhite()
    {
        var rgbw = ChannelLayout.Templates.Single(t => t.Name == "RGBW").Rows;

        Assert.Equal([ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White], rgbw.Select(r => r.Type));
    }

    [Fact]
    public void IsCustom_BuiltInProfile_False()
    {
        Assert.False(ChannelLayout.IsCustom(TestProfiles.Rgbw()));
    }

    [Fact]
    public void Serializer_CustomProfile_RoundTrips()
    {
        var profile = ChannelLayout.CreateProfile("Acme", "Odd", Rows(ChannelType.Green, ChannelType.Dimmer, ChannelType.Red, ChannelType.Fine));

        var json = ProfileSerializer.Serialize(profile);
        var back = ProfileSerializer.Deserialize(json);

        Assert.Contains("\"userDefined\": true", json, StringComparison.Ordinal);
        Assert.True(ChannelLayout.IsCustom(back));
        Assert.False(back.Verified);
        Assert.Equal(profile.Id, back.Id);
        Assert.Equal(profile.Modes[0].Channels, back.Modes[0].Channels);
        Assert.Equal(
            profile.Channels.Select(c => (c.Key, c.Name, c.Type, c.Default, c.CoarseKey)),
            back.Channels.Select(c => (c.Key, c.Name, c.Type, c.Default, c.CoarseKey)));
        Assert.Equal(json, ProfileSerializer.Serialize(back));
    }

    [Fact]
    public void Serializer_BuiltInProfile_OmitsUserDefined()
    {
        Assert.DoesNotContain("userDefined", ProfileSerializer.Serialize(TestProfiles.Rgbw()), StringComparison.Ordinal);
    }

    [Fact]
    public void Clone_CustomProfile_KeepsUserDefined()
    {
        Assert.True(ChannelLayout.CreateProfile("X", "P", Rows(ChannelType.Red)).Clone().UserDefined);
    }

    [Fact]
    public void Empty_Count_GenericRowsClamped()
    {
        Assert.Equal(3, ChannelLayout.Empty(3).Count(r => r.Type == ChannelType.Generic));
        Assert.Single(ChannelLayout.Empty(0));
        Assert.Equal(DmxAddress.UniverseSize, ChannelLayout.Empty(9999).Count);
    }
}
