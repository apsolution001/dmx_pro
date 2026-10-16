using DmxPro.Core.Profiles;

namespace DmxPro.Core.Tests;

public class ProfileLibraryTests
{
    private static string BuiltInProfilesDirectory => Path.Combine(AppContext.BaseDirectory, "profiles");

    [Fact]
    public async Task LoadDirectoryAsync_BuiltInProfiles_AllLoadWithoutErrors()
    {
        var library = new ProfileLibrary();

        var issues = await library.LoadDirectoryAsync(BuiltInProfilesDirectory);

        Assert.Empty(issues);
        Assert.NotNull(library.FindProfile("generic-rgb"));
        var rgbw = library.FindProfile("generic-rgbw")!;
        Assert.False(rgbw.Verified);
        Assert.Equal(8, rgbw.FindMode("8ch Full")!.ChannelCount);
    }

    [Fact]
    public async Task LoadDirectoryAsync_BrokenFile_ReportsAndContinues()
    {
        var dir = Directory.CreateTempSubdirectory("dmxpro-lib-");
        try
        {
            File.Copy(Path.Combine(BuiltInProfilesDirectory, "generic-rgb.dmxprofile.json"), Path.Combine(dir.FullName, "a.dmxprofile.json"));
            await File.WriteAllTextAsync(Path.Combine(dir.FullName, "b.dmxprofile.json"), "{ broken");
            var library = new ProfileLibrary();

            var issues = await library.LoadDirectoryAsync(dir.FullName);

            Assert.Equal("PROFILE_LOAD_FAILED", Assert.Single(issues).Code);
            Assert.Single(library.Profiles);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Add_InvalidProfile_IsRejected()
    {
        var library = new ProfileLibrary();
        var profile = TestProfiles.Rgbw();
        profile.Modes.Clear();

        library.Add(profile);

        Assert.Empty(library.Profiles);
    }

    [Fact]
    public void Deserialize_SerializedProfile_RoundTrips()
    {
        var profile = TestProfiles.Rgbw();
        profile.Channels[5].Capabilities.Add(new ChannelCapability { Min = 0, Max = 9, Label = "Off", Safe = true });

        var loaded = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(profile));

        Assert.Equal(profile.Channels.Count, loaded.Channels.Count);
        Assert.True(loaded.Channels[5].Capabilities[0].Safe);
        Assert.Equal(ChannelType.Strobe, loaded.Channels[5].Type);
    }
}
