using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>Color cycle, pulse and flash on the 8ch RGBW rig fixture (with dimmer) and extra fixtures.</summary>
public class ColorEffectPlaybackTests
{
    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor Green = new(0, 255, 0);
    private static readonly RgbColor White = new(255, 255, 255);

    // ───────────────────────────── Color cycle ─────────────────────────────

    [Fact]
    public void StartFunction_ColorCycle_HoldsThenCrossFadesAndLoops()
    {
        var rig = new TestRig();
        var cycle = rig.Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [rig.Fixture.Id], Colors = [Red, Green] });
        rig.Load();
        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal((255, 0, 0), Rgb(rig, TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Dimmer));

        rig.Tick(1); // 1.5 s: half way red → green
        Assert.Equal((128, 128, 0), Rgb(rig, TestRig.Red));

        rig.Tick(1); // 2.5 s: green hold
        Assert.Equal((0, 255, 0), Rgb(rig, TestRig.Red));

        rig.Tick(1); // 3.5 s: green → red
        Assert.Equal((128, 128, 0), Rgb(rig, TestRig.Red));

        rig.Tick(1); // 4.5 s: looped
        Assert.Equal((255, 0, 0), Rgb(rig, TestRig.Red));
    }

    [Fact]
    public void StartFunction_ColorCycleOnGrbFixture_ResolvesChannelsFromProfile()
    {
        var rig = new TestRig();
        var grb = rig.AddGrb(100);
        var cycle = rig.Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [grb.Id], Colors = [Red] });
        rig.Load();
        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal(0, rig.At(100)); // green
        Assert.Equal(255, rig.At(101)); // red
        Assert.Equal(0, rig.At(102)); // blue
    }

    [Fact]
    public void StartFunction_ColorCycleWithFixtureOffset_RunsFixturesBehindEachOther()
    {
        var rig = new TestRig();
        var grb = rig.AddGrb(100);
        var cycle = rig.Add(new ColorCycleFunction
        {
            Name = "Chase",
            FixtureIds = [rig.Fixture.Id, grb.Id],
            Colors = [Red, Green],
            FadeSeconds = 0,
            FixtureOffsetSeconds = 1,
        });
        rig.Load();
        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);

        rig.Tick(0.5);

        Assert.Equal(255, rig.At(TestRig.Red)); // first fixture: red
        Assert.Equal(255, rig.At(100)); // second fixture 1 s behind: still green (wraps to the previous colour)
        Assert.Equal(0, rig.At(101));
    }

    [Fact]
    public void StartFunction_ColorCycleDimmerOnlyFixture_IsLeftAlone()
    {
        var rig = new TestRig();
        var dimmer = rig.AddDimmer(200);
        var cycle = rig.Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [dimmer.Id], Colors = [Red] });
        rig.Load();
        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal(0, rig.At(200));
    }

    [Fact]
    public void StartFunction_UseWhiteChannel_MovesCommonPartToWhite()
    {
        var rig = new TestRig();
        var cycle = rig.Add(new ColorCycleFunction
        {
            Name = "Warm",
            FixtureIds = [rig.Fixture.Id],
            Colors = [new RgbColor(255, 200, 100)],
            UseWhiteChannel = true,
        });
        rig.Load();
        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal((155, 100, 0), Rgb(rig, TestRig.Red));
        Assert.Equal(100, rig.At(TestRig.White));
    }

    [Fact]
    public void StartFunction_ColorEffectOverWhiteScene_ZeroesWhiteChannel()
    {
        var rig = new TestRig();
        var white = rig.Scene("W", ("w", 255));
        var cycle = rig.Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [rig.Fixture.Id], Colors = [Red] });
        rig.Load();
        rig.Engine.StartFunction(white.Id, EngineLayer.Show);
        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.White)); // the console colour owns the fixture's colour channels
    }

    // ───────────────────────────── Pulse ─────────────────────────────

    [Fact]
    public void StartFunction_PulseSineWithDimmer_BreathesOnDimmerWithColourAtFull()
    {
        var rig = new TestRig();
        var pulse = rig.Add(new PulseFunction { Name = "Pulse", FixtureIds = [rig.Fixture.Id], PeriodSeconds = 2 });
        rig.Load();
        rig.Engine.StartFunction(pulse.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal(128, rig.At(TestRig.Dimmer)); // 0.5 − 0.5·cos(π/2) = 0.5 → 127.5
        Assert.Equal((255, 255, 255), Rgb(rig, TestRig.Red));

        rig.Tick(0.5);
        Assert.Equal(255, rig.At(TestRig.Dimmer)); // peak at half the period

        rig.Tick(1);
        Assert.Equal(0, rig.At(TestRig.Dimmer)); // back to the minimum
    }

    [Fact]
    public void StartFunction_PulseTriangleWithLimits_StaysBetweenMinAndMax()
    {
        var rig = new TestRig();
        var pulse = rig.Add(new PulseFunction
        {
            Name = "Pulse",
            FixtureIds = [rig.Fixture.Id],
            Shape = PulseShape.Triangle,
            PeriodSeconds = 2,
            MinLevel = 0.2,
            MaxLevel = 0.6,
        });
        rig.Load();
        rig.Engine.StartFunction(pulse.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal(102, rig.At(TestRig.Dimmer)); // 0.2 + 0.4 × 0.5 = 0.4 → 102

        rig.Tick(0.5);
        Assert.Equal(153, rig.At(TestRig.Dimmer)); // 0.6

        rig.Tick(1);
        Assert.Equal(51, rig.At(TestRig.Dimmer)); // 0.2
    }

    [Fact]
    public void StartFunction_PulseWithoutDimmer_ScalesColourChannels()
    {
        var rig = new TestRig();
        var grb = rig.AddGrb(100);
        var pulse = rig.Add(new PulseFunction { Name = "Pulse", FixtureIds = [grb.Id], Color = Red, Shape = PulseShape.Triangle, PeriodSeconds = 2 });
        rig.Load();
        rig.Engine.StartFunction(pulse.Id, EngineLayer.Console);

        rig.Tick(0.5);

        Assert.Equal(128, rig.At(101)); // red × 0.5
        Assert.Equal(0, rig.At(100));
    }

    [Fact]
    public void StartFunction_PulseWithoutColour_TouchesOnlyDimmers()
    {
        var rig = new TestRig();
        var grb = rig.AddGrb(100);
        var blue = rig.Scene("Blue", ("b", 255));
        var pulse = rig.Add(new PulseFunction { Name = "Pulse", FixtureIds = [rig.Fixture.Id, grb.Id], Color = null, Shape = PulseShape.Triangle, PeriodSeconds = 2 });
        rig.Load();
        rig.Engine.StartFunction(blue.Id, EngineLayer.Show);
        rig.Engine.StartFunction(pulse.Id, EngineLayer.Console);

        rig.Tick(0.5);

        Assert.Equal(128, rig.At(TestRig.Dimmer));
        Assert.Equal(255, rig.At(TestRig.Blue)); // colour from underneath
        Assert.Equal(0, rig.At(101)); // GRB fixture without dimmer: untouched
    }

    [Fact]
    public void StartFunction_PulseVeryShortPeriod_IsSlowedToMinimum()
    {
        var rig = new TestRig();
        var pulse = rig.Add(new PulseFunction { Name = "Pulse", FixtureIds = [rig.Fixture.Id], Shape = PulseShape.Triangle, PeriodSeconds = 0.001 });
        rig.Load();
        rig.Engine.StartFunction(pulse.Id, EngineLayer.Console);

        rig.Tick(0.025); // a quarter of the 0.1 s minimum period

        Assert.Equal(128, rig.At(TestRig.Dimmer));
    }

    // ───────────────────────────── Flash ─────────────────────────────

    [Fact]
    public void StartFunction_Flash_FullColourAtOnceAndReleasedOnStop()
    {
        var rig = new TestRig();
        var dimmer = rig.AddDimmer(200);
        var flash = rig.Add(new FlashFunction { Name = "Flash", FixtureIds = [rig.Fixture.Id, dimmer.Id], Color = Red });
        rig.Load();

        rig.Engine.StartFunction(flash.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal((255, 0, 0), Rgb(rig, TestRig.Red));
        Assert.Equal(255, rig.At(200)); // dimmer-only fixture: brightest component

        rig.Engine.StopFunction(flash.Id, EngineLayer.Console);
        rig.Tick();
        var expected = TestRig.DefaultsFrame();
        Assert.Equal(expected, rig.Universe());
    }

    [Fact]
    public void StartFunction_FlashWithDuration_ReleasesByItself()
    {
        var rig = new TestRig();
        var flash = rig.Add(new FlashFunction { Name = "Hit", FixtureIds = [rig.Fixture.Id], Color = White, DurationSeconds = 0.5 });
        rig.Load();
        rig.Engine.StartFunction(flash.Id, EngineLayer.Console);

        rig.Tick(0.25);
        Assert.Equal(255, rig.At(TestRig.Green));

        rig.Tick(0.5);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void ReleaseAll_RunningEffects_ReturnToDefaults()
    {
        var rig = new TestRig();
        var flash = rig.Add(new FlashFunction { Name = "Flash", FixtureIds = [rig.Fixture.Id] });
        var pulse = rig.Add(new PulseFunction { Name = "Pulse", FixtureIds = [rig.Fixture.Id] });
        rig.Load();
        rig.Engine.StartFunction(flash.Id, EngineLayer.Console);
        rig.Engine.StartFunction(pulse.Id, EngineLayer.Show);
        rig.Tick(0.5);

        rig.Engine.ReleaseAll();
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartFunction_EffectWithMissingFixture_SkipsIt()
    {
        var rig = new TestRig();
        var flash = rig.Add(new FlashFunction { Name = "Flash", FixtureIds = [Guid.NewGuid(), rig.Fixture.Id], Color = Green });
        rig.Load();
        rig.Engine.StartFunction(flash.Id, EngineLayer.Console);

        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Green));
    }

    private static (int R, int G, int B) Rgb(TestRig rig, int redAddress) =>
        (rig.At(redAddress), rig.At(redAddress + 1), rig.At(redAddress + 2));
}
