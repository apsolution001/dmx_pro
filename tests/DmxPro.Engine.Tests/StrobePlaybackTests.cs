using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>Strobe function and safety rule 8 (allow strobe, maximum strobe rate).</summary>
public class StrobePlaybackTests
{
    private static (TestRig Rig, StrobeFunction Strobe) RigWithStrobe(Action<StrobeFunction>? configure = null)
    {
        var rig = new TestRig();
        var strobe = rig.Add(new StrobeFunction { Name = "Strobe", FixtureIds = [rig.Fixture.Id], RateHz = 5 });
        configure?.Invoke(strobe);
        rig.Load();
        return (rig, strobe);
    }

    private static ActiveFunctionInfo Info(TestRig rig) => Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);

    [Fact]
    public void StartFunction_StrobeNotAllowed_SteadyLightAndReportsSuppressed()
    {
        var (rig, strobe) = RigWithStrobe();
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        for (var i = 0; i < 20; i++)
        {
            rig.Tick();
            Assert.Equal(255, rig.At(TestRig.Dimmer));
            Assert.Equal(255, rig.At(TestRig.Red));
        }

        Assert.Equal(StrobeLimit.Suppressed, Info(rig).StrobeLimit);
        Assert.Equal(StrobeLimit.Suppressed, rig.Engine.GetSnapshot()!.StrobeLimit);
    }

    [Fact]
    public void StartFunction_SoftwareStrobeAllowed_SwitchesDimmerOnAndOff()
    {
        var (rig, strobe) = RigWithStrobe();
        rig.Engine.AllowStrobe = true;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick(0.05); // 5 Hz, duty 0.5: on for 0.1 s of each 0.2 s
        Assert.Equal(255, rig.At(TestRig.Dimmer));

        rig.Tick(0.1); // 0.15 s
        Assert.Equal(0, rig.At(TestRig.Dimmer));
        Assert.Equal(255, rig.At(TestRig.Red)); // colour stays; the dimmer carries the flash

        rig.Tick(0.1); // 0.25 s
        Assert.Equal(255, rig.At(TestRig.Dimmer));
        Assert.Equal(StrobeLimit.None, Info(rig).StrobeLimit);
    }

    [Fact]
    public void StartFunction_SoftwareStrobeWithoutDimmer_SwitchesColourChannels()
    {
        var rig = new TestRig();
        var grb = rig.AddGrb(100);
        var strobe = rig.Add(new StrobeFunction { Name = "Strobe", FixtureIds = [grb.Id], RateHz = 5, Color = new RgbColor(0, 0, 255) });
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick(0.05);
        Assert.Equal(255, rig.At(102));

        rig.Tick(0.1);
        Assert.Equal(0, rig.At(102));
    }

    [Fact]
    public void StartFunction_RateAboveMaximum_IsCappedAndReportsRateLimited()
    {
        var (rig, strobe) = RigWithStrobe(s => s.RateHz = 20);
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 10;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick(0.07); // at 10 Hz: phase 0.7 → off (at 20 Hz it would be phase 0.4 → on)

        Assert.Equal(0, rig.At(TestRig.Dimmer));
        Assert.Equal(StrobeLimit.RateLimited, Info(rig).StrobeLimit);
        Assert.Equal(StrobeLimit.RateLimited, rig.Engine.GetSnapshot()!.StrobeLimit);
    }

    [Fact]
    public void SetFunctionSpeed_SpedUpStrobeAboveMaximum_IsCapped()
    {
        var (rig, strobe) = RigWithStrobe();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 10;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);
        rig.Engine.SetFunctionSpeed(strobe.Id, EngineLayer.Console, 4); // 20 Hz requested

        rig.Tick(0.07);

        Assert.Equal(0, rig.At(TestRig.Dimmer)); // played at 10 Hz real time
        Assert.Equal(StrobeLimit.RateLimited, Info(rig).StrobeLimit);
    }

    [Fact]
    public void StartFunction_MaxStrobeRateZero_SteadyLightAndReportsSuppressed()
    {
        var (rig, strobe) = RigWithStrobe();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 0;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick(0.15);

        Assert.Equal(255, rig.At(TestRig.Dimmer));
        Assert.Equal(StrobeLimit.Suppressed, Info(rig).StrobeLimit);
    }

    [Fact]
    public void StartFunction_HardwareStrobe_UsesFirstDocumentedStrobingValue()
    {
        var (rig, strobe) = RigWithStrobe(s => s.Mode = StrobeMode.Hardware);
        rig.Engine.AllowStrobe = true;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick(0.15);

        Assert.Equal(10, rig.At(TestRig.Strobe)); // start of "Strobe slow to fast"
        Assert.Equal(255, rig.At(TestRig.Dimmer)); // light stays on; the fixture flashes itself
    }

    [Fact]
    public void StartFunction_HardwareStrobeWithValue_SendsThatValue()
    {
        var (rig, strobe) = RigWithStrobe(s =>
        {
            s.Mode = StrobeMode.Hardware;
            s.HardwareValue = 200;
        });
        rig.Engine.AllowStrobe = true;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal(200, rig.At(TestRig.Strobe));
    }

    [Fact]
    public void StartFunction_HardwareStrobeNotAllowed_StrobeChannelSafeAndSuppressed()
    {
        var (rig, strobe) = RigWithStrobe(s =>
        {
            s.Mode = StrobeMode.Hardware;
            s.HardwareValue = 200;
        });
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Strobe));
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(StrobeLimit.Suppressed, Info(rig).StrobeLimit);
    }

    [Fact]
    public void StartFunction_HardwareStrobeFixtureWithoutStrobeChannel_FallsBackToSoftware()
    {
        var rig = new TestRig(mode: "5ch", startAddress: 1);
        var strobe = rig.Add(new StrobeFunction { Name = "Strobe", FixtureIds = [rig.Fixture.Id], RateHz = 5, Mode = StrobeMode.Hardware });
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.StartFunction(strobe.Id, EngineLayer.Console);

        rig.Tick(0.15);

        Assert.Equal(0, rig.At(1)); // 5ch: dimmer at the start address, switched off by the software strobe
    }

    [Fact]
    public void StartShow_StrobeItemNotAllowed_ReportsSuppressedInSnapshot()
    {
        var (rig, strobe) = RigWithStrobe();
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 10;
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, strobe.Id, 0, 5);
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id);

        rig.Tick();

        Assert.Equal(StrobeLimit.Suppressed, rig.Engine.GetSnapshot()!.StrobeLimit);
        Assert.Equal(255, rig.At(TestRig.Dimmer));
    }

    [Fact]
    public void Tick_NoStrobeRunning_ReportsNoLimit()
    {
        var (rig, _) = RigWithStrobe();

        rig.Tick();

        Assert.Equal(StrobeLimit.None, rig.Engine.GetSnapshot()!.StrobeLimit);
    }
}
