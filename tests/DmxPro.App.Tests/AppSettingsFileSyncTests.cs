using DmxPro.App.Services;

namespace DmxPro.App.Tests;

public sealed class AppSettingsFileSyncTests
{
    [Fact]
    public void New_Defaults_AutoSaveEveryFiveMinutesAndLiveReloadOn()
    {
        var settings = new AppSettings();

        Assert.True(settings.AutoSaveEnabled);
        Assert.Equal(5, settings.AutosaveIntervalMinutes);
        Assert.True(settings.ReloadProjectOnDiskChange);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-3, 5)]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(120, 60)]
    public void Normalize_Interval_ClampedToOneToSixtyAndOldZeroBecomesDefault(int stored, int expected)
    {
        var settings = new AppSettings { AutosaveIntervalMinutes = stored };

        settings.Normalize();

        Assert.Equal(expected, settings.AutosaveIntervalMinutes);
    }
}
