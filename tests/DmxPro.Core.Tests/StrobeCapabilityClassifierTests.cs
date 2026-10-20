using DmxPro.Core.Profiles;

namespace DmxPro.Core.Tests;

public class StrobeCapabilityClassifierTests
{
    [Theory]
    [InlineData("No function", StrobeRangeKind.Steady)]
    [InlineData("NO STROBE", StrobeRangeKind.Steady)]
    [InlineData("Strobe off", StrobeRangeKind.Steady)]
    [InlineData("Shutter open", StrobeRangeKind.Steady)]
    [InlineData("Open", StrobeRangeKind.Steady)]
    [InlineData("LED on", StrobeRangeKind.Steady)]
    [InlineData("Off", StrobeRangeKind.Steady)]
    [InlineData("On", StrobeRangeKind.Steady)]
    [InlineData("Shutter closed", StrobeRangeKind.Closed)]
    [InlineData("Blackout", StrobeRangeKind.Closed)]
    [InlineData("Close", StrobeRangeKind.Closed)]
    [InlineData("Shutter closed / no strobe", StrobeRangeKind.Closed)]
    [InlineData("Strobe slow to fast", StrobeRangeKind.Strobing)]
    [InlineData("Random strobe", StrobeRangeKind.Strobing)]
    [InlineData("Pulse effect", StrobeRangeKind.Strobing)]
    [InlineData("Ramp up slow -> fast", StrobeRangeKind.Strobing)]
    [InlineData("Lightning", StrobeRangeKind.Strobing)]
    [InlineData("Flash", StrobeRangeKind.Strobing)]
    [InlineData("Open, strobe slow to fast", StrobeRangeKind.Strobing)]
    [InlineData("Mode A", StrobeRangeKind.Unknown)]
    [InlineData("", StrobeRangeKind.Unknown)]
    [InlineData("   ", StrobeRangeKind.Unknown)]
    public void Classify_Label_ReturnsKind(string label, StrobeRangeKind expected)
    {
        Assert.Equal(expected, StrobeCapabilityClassifier.Classify(new ChannelCapability { Label = label }));
    }

    [Theory]
    [InlineData("ShutterOpen", StrobeRangeKind.Steady)]
    [InlineData("ShutterClose", StrobeRangeKind.Closed)]
    [InlineData("StrobeSlowToFast", StrobeRangeKind.Strobing)]
    [InlineData("StrobeFastToSlow", StrobeRangeKind.Strobing)]
    [InlineData("StrobeRandom", StrobeRangeKind.Strobing)]
    [InlineData("StrobeRandomSlowToFast", StrobeRangeKind.Strobing)]
    [InlineData("StrobeFrequency", StrobeRangeKind.Strobing)]
    [InlineData("PulseSlowToFast", StrobeRangeKind.Strobing)]
    [InlineData("RampUpSlowToFast", StrobeRangeKind.Strobing)]
    [InlineData("RampDownFastToSlow", StrobeRangeKind.Strobing)]
    public void Classify_PresetWithoutLabel_ReturnsKind(string preset, StrobeRangeKind expected)
    {
        Assert.Equal(expected, StrobeCapabilityClassifier.Classify(new ChannelCapability { Preset = preset }));
    }

    [Fact]
    public void Classify_PresetDisagreesWithLabel_PresetWins()
    {
        var capability = new ChannelCapability { Label = "Strobe effect", Preset = "ShutterOpen" };

        Assert.Equal(StrobeRangeKind.Steady, StrobeCapabilityClassifier.Classify(capability));
    }

    [Fact]
    public void Classify_UnrelatedPreset_FallsBackToLabel()
    {
        var capability = new ChannelCapability { Label = "Strobe slow to fast", Preset = "Alias" };

        Assert.Equal(StrobeRangeKind.Strobing, StrobeCapabilityClassifier.Classify(capability));
    }

    [Fact]
    public void Classify_SafeFlag_IsSteadyWhateverTheLabel()
    {
        var capability = new ChannelCapability { Label = "Strobe slow to fast", Preset = "StrobeSlowToFast", Safe = true };

        Assert.Equal(StrobeRangeKind.Steady, StrobeCapabilityClassifier.Classify(capability));
    }

    [Fact]
    public void Classify_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StrobeCapabilityClassifier.Classify(null!));
    }
}
