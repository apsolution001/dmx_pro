using DmxPro.Core.Functions;
using DmxPro.Core.Profiles;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Strobe safety on profiles whose resting range is not flagged <see cref="ChannelCapability.Safe"/> (e.g. imported from
/// QLC+ .qxf), using the shared <see cref="StrobeCapabilityClassifier"/>.
/// </summary>
public class StrobeSafeValueTests
{
    private static TestRig Rig(byte defaultValue, params ChannelCapability[] capabilities)
    {
        var rig = new TestRig();
        var strobe = rig.Project.FindProfile("test-rgbw")!.FindChannel("strobe")!;
        strobe.Default = defaultValue;
        strobe.Capabilities = [.. capabilities];
        return rig;
    }

    private static byte StrobeWhileNotAllowed(TestRig rig, byte requested)
    {
        var scene = rig.Scene("S", ("strobe", requested));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();
        Assert.False(rig.Engine.AllowStrobe);
        return rig.At(TestRig.Strobe);
    }

    [Fact]
    public void AllowStrobeFalse_NoFunctionRangeWithoutSafeFlag_HoldsAtDefaultInThatRange()
    {
        var rig = Rig(
            0,
            new ChannelCapability { Min = 0, Max = 9, Label = "No function" },
            new ChannelCapability { Min = 10, Max = 255, Label = "Strobe slow to fast" });

        Assert.Equal(0, StrobeWhileNotAllowed(rig, 200));
    }

    [Fact]
    public void AllowStrobeFalse_DefaultStrobesAndOpenLabelRange_UsesOpenRangeStart()
    {
        var rig = Rig(
            200,
            new ChannelCapability { Min = 0, Max = 4, Label = "Shutter closed" },
            new ChannelCapability { Min = 5, Max = 9, Label = "Shutter open" },
            new ChannelCapability { Min = 10, Max = 255, Label = "Strobe" });

        Assert.Equal(5, StrobeWhileNotAllowed(rig, 200));
    }

    [Fact]
    public void AllowStrobeFalse_ShutterOpenPresetWithoutLabel_UsesPresetRange()
    {
        var rig = Rig(
            100,
            new ChannelCapability { Min = 0, Max = 19, Preset = "ShutterClose" },
            new ChannelCapability { Min = 20, Max = 29, Preset = "ShutterOpen" },
            new ChannelCapability { Min = 30, Max = 255, Preset = "StrobeSlowToFast" });

        Assert.Equal(20, StrobeWhileNotAllowed(rig, 200));
    }

    [Fact]
    public void AllowStrobeFalse_NoSteadyRangeAndDefaultStrobes_UsesClosedRangeNotAStrobingValue()
    {
        var rig = Rig(
            128,
            new ChannelCapability { Min = 0, Max = 4, Label = "Shutter closed" },
            new ChannelCapability { Min = 5, Max = 255, Label = "Strobe slow to fast" });

        Assert.Equal(0, StrobeWhileNotAllowed(rig, 200));
    }

    [Fact]
    public void AllowStrobeFalse_NoCapabilities_HoldsAtDefault()
    {
        var rig = Rig(0);

        Assert.Equal(0, StrobeWhileNotAllowed(rig, 200));
    }
}
