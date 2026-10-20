using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Tests;

public class BuiltInProfilesTests
{
    private static string RepoProfilesDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DmxPro.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "profiles");
        }
    }

    private static List<FixtureProfile> LoadAll() =>
        Directory.GetFiles(RepoProfilesDirectory, "*" + ProfileSerializer.FileExtension)
            .Select(f => ProfileSerializer.Deserialize(File.ReadAllText(f)))
            .ToList();

    [Fact]
    public void Validate_EveryRepoProfile_HasNoErrors()
    {
        var profiles = LoadAll();

        Assert.NotEmpty(profiles);
        foreach (var profile in profiles)
        {
            Assert.DoesNotContain(profile.Validate(), i => i.Severity == ValidationSeverity.Error);
        }
    }

    [Fact]
    public void Id_EveryRepoProfile_IsUnique()
    {
        var ids = LoadAll().Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("generic-dimmer", "1ch Dimmer", new[] { ChannelType.Dimmer })]
    [InlineData("generic-rgba", "4ch RGBA", new[] { ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.Amber })]
    [InlineData("generic-rgba", "5ch Dimmer RGBA", new[] { ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.Amber })]
    [InlineData("generic-rgbwa", "5ch RGBWA", new[] { ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber })]
    [InlineData("generic-rgbwa", "6ch Dimmer RGBWA", new[] { ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber })]
    [InlineData("generic-rgbwauv", "6ch RGBWAUV", new[] { ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV })]
    [InlineData("generic-rgbwauv", "7ch Dimmer RGBWAUV", new[] { ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV })]
    public void ResolveChannels_GenericMode_HasExpectedChannelTypesInOrder(string id, string modeName, ChannelType[] expected)
    {
        var profile = LoadAll().Single(p => p.Id == id);
        var mode = profile.FindMode(modeName);

        Assert.NotNull(mode);
        Assert.Equal(expected, profile.ResolveChannels(mode).Select(c => c.Type).ToArray());
        Assert.False(profile.Verified);
        Assert.Equal("Generic", profile.Manufacturer);
    }
}
