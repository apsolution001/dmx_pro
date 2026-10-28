using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

/// <summary>
/// Retired colour cycle, fade, pulse, flash and strobe functions become scenes and chasers with the same id, name and
/// ownership (owner decision 2026-10-28).
/// </summary>
public class LegacyFunctionMigrationTests
{
    private readonly ProjectSerializer _serializer = new();
    private readonly Project _project = new();

    /// <summary>RGBW + Dimmer + undocumented Strobe (8ch mode of <see cref="TestProfiles.Rgbw"/>).</summary>
    private readonly Fixture _par;

    /// <summary>RGBW without Dimmer or Strobe (4ch mode).</summary>
    private readonly Fixture _bar;

    /// <summary>Dimmer + RGB + a Strobe channel with a documented strobing range (10–255).</summary>
    private readonly Fixture _strobePar;

    public LegacyFunctionMigrationTests()
    {
        _par = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
        _bar = _project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 20, "BAR")[0];
        _strobePar = _project.AddFixtures(StrobeProfile(), "5ch", 1, 1, 40, "STROBE")[0];
    }

    private static FixtureProfile StrobeProfile() => new()
    {
        Id = "test-strobe-par",
        Manufacturer = "Test",
        Model = "Strobe PAR",
        Channels =
        [
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer },
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
            new()
            {
                Key = "shutter",
                Name = "Shutter",
                Type = ChannelType.Strobe,
                Capabilities =
                [
                    new() { Min = 0, Max = 9, Label = "No strobe", Safe = true },
                    new() { Min = 10, Max = 255, Label = "Strobe slow to fast" },
                ],
            },
        ],
        Modes = [new() { Name = "5ch", Channels = ["dim", "r", "g", "b", "shutter"] }],
    };

    private static FixtureProfile ShutterProfile(string id, byte shutterDefault, params ChannelCapability[] capabilities) => new()
    {
        Id = id,
        Manufacturer = "Test",
        Model = id,
        Channels =
        [
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer },
            new() { Key = "shutter", Name = "Shutter", Type = ChannelType.Strobe, Default = shutterDefault, Capabilities = [.. capabilities] },
        ],
        Modes = [new() { Name = "2ch", Channels = ["dim", "shutter"] }],
    };

    /// <summary>The value of <paramref name="key"/> in <paramref name="look"/> is unset or not in a strobing range of <see cref="StrobeProfile"/>.</summary>
    private static void AssertNotStrobing(SceneFunction look, Guid fixtureId, string key)
    {
        if (look.GetValue(fixtureId, key) is not { } value)
        {
            return;
        }

        var range = StrobeProfile().Channels.Single(c => c.Key == key).FindCapability(value);
        Assert.NotNull(range);
        Assert.NotEqual(StrobeRangeKind.Strobing, StrobeCapabilityClassifier.Classify(range));
    }

    private T Add<T>(T function)
        where T : LightFunction
    {
        _project.Functions.Add(function);
        return function;
    }

    private ChaserFunction Chaser(Guid id) => Assert.IsType<ChaserFunction>(_project.FindFunction(id));

    private SceneFunction Step(ChaserFunction chaser, int index)
    {
        var scene = Assert.IsType<SceneFunction>(_project.FindFunction(chaser.Steps[index].FunctionId));
        Assert.Equal(chaser.Id, scene.OwnerFunctionId);
        Assert.False(scene.ShowInFunctionList);
        Assert.Null(scene.OwnerShowId);
        return scene;
    }

    private static (double FadeIn, double Hold, double FadeOut) Times(ChaserStep step) =>
        (step.FadeInSeconds, step.HoldSeconds, step.FadeOutSeconds);

    // ───────────────────────────── Colour cycle ─────────────────────────────

    [Fact]
    public void Migrate_ColorCycle_BecomesLoopingChaserWithOneCrossfadingStepPerColour()
    {
        var cycle = Add(new ColorCycleFunction
        {
            Name = "Rainbow",
            FixtureIds = [_par.Id],
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255)],
            HoldSeconds = 2,
            FadeSeconds = 0.5,
        });
        var index = _project.Functions.IndexOf(cycle);

        var converted = LegacyFunctionMigration.Migrate(_project);

        Assert.Equal(1, converted);
        var chaser = Chaser(cycle.Id);
        Assert.Same(chaser, _project.Functions[index]);
        Assert.Equal("Rainbow", chaser.Name);
        Assert.Equal(RunOrder.Loop, chaser.RunOrder);
        Assert.Equal(RunDirection.Forward, chaser.Direction);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.All(chaser.Steps, s => Assert.Equal((0.5, 2.0, 0.5), Times(s)));

        var red = Step(chaser, 0);
        var blue = Step(chaser, 1);
        Assert.Equal("Color 1", red.Name);
        Assert.Same(red, _project.Functions[index + 1]);
        Assert.Equal((byte?)255, red.GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)255, red.GetValue(_par.Id, "r"));
        Assert.Equal((byte?)0, red.GetValue(_par.Id, "b"));
        Assert.Equal((byte?)0, red.GetValue(_par.Id, "w"));
        Assert.Equal((byte?)255, blue.GetValue(_par.Id, "b"));
        Assert.Null(red.GetValue(_par.Id, "strobe"));
        Assert.Equal(0, red.FadeInSeconds);
        Assert.Equal(0, red.FadeOutSeconds);
    }

    [Fact]
    public void Migrate_ColorCycleUseWhite_PutsCommonPartOnWhiteChannel()
    {
        var cycle = Add(new ColorCycleFunction
        {
            FixtureIds = [_bar.Id],
            Colors = [new RgbColor(255, 255, 200), new RgbColor(0, 0, 0)],
            UseWhiteChannel = true,
        });

        LegacyFunctionMigration.Migrate(_project);

        var look = Step(Chaser(cycle.Id), 0);
        Assert.Equal((byte?)200, look.GetValue(_bar.Id, "w"));
        Assert.Equal((byte?)55, look.GetValue(_bar.Id, "r"));
        Assert.Equal((byte?)55, look.GetValue(_bar.Id, "g"));
        Assert.Equal((byte?)0, look.GetValue(_bar.Id, "b"));
        Assert.Null(look.GetValue(_bar.Id, "dim"));
    }

    [Fact]
    public void Migrate_ColorCycleTooShort_SlotStretchedToOneStep()
    {
        var cycle = Add(new ColorCycleFunction { FixtureIds = [_par.Id], HoldSeconds = 0, FadeSeconds = 0 });

        LegacyFunctionMigration.Migrate(_project);

        Assert.All(Chaser(cycle.Id).Steps, s => Assert.Equal((0.0, FunctionEditing.MinStepSeconds, 0.0), Times(s)));
    }

    [Fact]
    public void Migrate_ColorCycleSingleColour_OneSteadyStep()
    {
        var cycle = Add(new ColorCycleFunction { FixtureIds = [_par.Id], Colors = [new RgbColor(0, 255, 0)], HoldSeconds = 1, FadeSeconds = 1 });

        LegacyFunctionMigration.Migrate(_project);

        var step = Assert.Single(Chaser(cycle.Id).Steps);
        Assert.Equal((0.0, 2.0, 0.0), Times(step));
    }

    [Fact]
    public void Migrate_ColorCycleWithOffset_StepsFollowEveryLightsPhase()
    {
        // Slot 2 s, cycle 4 s, light 2 runs 1 s behind: a change happens every second.
        var cycle = Add(new ColorCycleFunction
        {
            FixtureIds = [_bar.Id, _par.Id],
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255)],
            HoldSeconds = 1,
            FadeSeconds = 1,
            FixtureOffsetSeconds = 1,
        });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(cycle.Id);
        Assert.Equal(4, chaser.Steps.Count);
        Assert.All(chaser.Steps, s => Assert.Equal((1.0, 0.0, 1.0), Times(s)));

        // t=0: light 1 red; light 2 (at -1 s = 3 s) has just reached blue. t=1: both red. t=2: blue / red. t=3: both blue.
        (byte R, byte B)[][] expected =
        [
            [(255, 0), (0, 255)],
            [(255, 0), (255, 0)],
            [(0, 255), (255, 0)],
            [(0, 255), (0, 255)],
        ];
        for (var i = 0; i < 4; i++)
        {
            var look = Step(chaser, i);
            Assert.Equal(expected[i][0], (look.GetValue(_bar.Id, "r")!.Value, look.GetValue(_bar.Id, "b")!.Value));
            Assert.Equal(expected[i][1], (look.GetValue(_par.Id, "r")!.Value, look.GetValue(_par.Id, "b")!.Value));
        }
    }

    [Fact]
    public void Migrate_ColorCycleWithOffsetNoFade_HardCutsAtEachLightsChange()
    {
        var cycle = Add(new ColorCycleFunction
        {
            FixtureIds = [_bar.Id, _par.Id],
            Colors = [new RgbColor(255, 0, 0), new RgbColor(0, 0, 255)],
            HoldSeconds = 2,
            FadeSeconds = 0,
            FixtureOffsetSeconds = 0.5,
        });

        LegacyFunctionMigration.Migrate(_project);

        // Changes at 0 (light 1 → red), 0.5 (light 2 → red), 2 (light 1 → blue), 2.5 (light 2 → blue); cuts, from t = 0.
        var chaser = Chaser(cycle.Id);
        Assert.Equal([0.5, 1.5, 0.5, 1.5], chaser.Steps.Select(s => s.HoldSeconds));
        Assert.All(chaser.Steps, s => Assert.Equal(0, s.FadeInSeconds));
        Assert.All(chaser.Steps, s => Assert.Equal(0, s.FadeOutSeconds));
        (byte Bar, byte Par)[] reds = [(255, 0), (255, 255), (0, 255), (0, 0)];
        for (var i = 0; i < 4; i++)
        {
            var look = Step(chaser, i);
            Assert.Equal(reds[i], (look.GetValue(_bar.Id, "r")!.Value, look.GetValue(_par.Id, "r")!.Value));
        }
    }

    [Fact]
    public void Migrate_ColorCycleOnlyDimmerOrDeletedLights_EmptyChaser()
    {
        var dimmerOnly = new FixtureProfile
        {
            Id = "dim-only",
            Model = "Dimmer",
            Channels = [new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer }],
            Modes = [new() { Name = "1ch", Channels = ["dim"] }],
        };
        var dimmer = _project.AddFixtures(dimmerOnly, "1ch", 1, 1, 100, "DIM")[0];
        var cycle = Add(new ColorCycleFunction { FixtureIds = [dimmer.Id, Guid.NewGuid()] });
        var noColours = Add(new ColorCycleFunction { FixtureIds = [_par.Id], Colors = null! });

        LegacyFunctionMigration.Migrate(_project);

        Assert.Empty(Chaser(cycle.Id).Steps);
        Assert.Empty(Chaser(noColours.Id).Steps);
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    // ───────────────────────────── Fade ─────────────────────────────

    [Fact]
    public void Migrate_Fade_SingleShotChaserFromStepThenFadingToStep()
    {
        var from = _project.CreateScene("Red");
        from.SetValue(_par.Id, "r", 255);
        var to = _project.CreateScene("Blue");
        to.SetValue(_par.Id, "b", 200);
        var fade = Add(new FadeFunction { Name = "Sunrise", FromSceneId = from.Id, ToSceneId = to.Id, DurationSeconds = 12 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(fade.Id);
        Assert.Equal("Sunrise", chaser.Name);
        Assert.Equal(RunOrder.SingleShot, chaser.RunOrder);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.Equal((0.0, 0.0, 12.0), Times(chaser.Steps[0]));
        Assert.Equal((12.0, FunctionEditing.MaxSeconds, 0.0), Times(chaser.Steps[1]));

        var fromCopy = Step(chaser, 0);
        var toCopy = Step(chaser, 1);
        Assert.NotEqual(from.Id, fromCopy.Id);
        Assert.Equal("Red", fromCopy.Name);
        Assert.Equal((byte?)255, fromCopy.GetValue(_par.Id, "r"));
        Assert.Equal((byte?)200, toCopy.GetValue(_par.Id, "b"));

        // The library scenes stay as they were.
        Assert.Null(from.OwnerFunctionId);
        Assert.Contains(from, _project.FunctionListItems);
    }

    [Fact]
    public void Migrate_FadeFromCurrentLook_OnlyToStepWithFadeIn()
    {
        var to = _project.CreateScene("Blue");
        to.SetValue(_par.Id, "b", 200);
        var fade = Add(new FadeFunction { ToSceneId = to.Id, DurationSeconds = 3 });

        LegacyFunctionMigration.Migrate(_project);

        var step = Assert.Single(Chaser(fade.Id).Steps);
        Assert.Equal((3.0, FunctionEditing.MaxSeconds, 0.0), Times(step));
    }

    [Fact]
    public void Migrate_FadeFromMissingOrNotAScene_OnlyToStep()
    {
        var to = _project.CreateScene("Blue");
        var flash = Add(new FlashFunction { Name = "Hit", FixtureIds = [_par.Id] });
        var missing = Add(new FadeFunction { FromSceneId = Guid.NewGuid(), ToSceneId = to.Id });
        var fromFlash = Add(new FadeFunction { FromSceneId = flash.Id, ToSceneId = to.Id });

        LegacyFunctionMigration.Migrate(_project);

        Assert.Single(Chaser(missing.Id).Steps);
        Assert.Single(Chaser(fromFlash.Id).Steps);
    }

    [Fact]
    public void Migrate_FadeWithoutTo_EmptyChaser()
    {
        var from = _project.CreateScene("Red");
        var none = Add(new FadeFunction { FromSceneId = from.Id });
        var missing = Add(new FadeFunction { FromSceneId = from.Id, ToSceneId = Guid.NewGuid() });

        LegacyFunctionMigration.Migrate(_project);

        Assert.Empty(Chaser(none.Id).Steps);
        Assert.Empty(Chaser(missing.Id).Steps);
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void Migrate_FadeZeroDuration_OnlyToStepNoFade()
    {
        var from = _project.CreateScene("Red");
        var to = _project.CreateScene("Blue");
        var fade = Add(new FadeFunction { FromSceneId = from.Id, ToSceneId = to.Id, DurationSeconds = 0 });

        LegacyFunctionMigration.Migrate(_project);

        Assert.Equal((0.0, FunctionEditing.MaxSeconds, 0.0), Times(Assert.Single(Chaser(fade.Id).Steps)));
    }

    [Fact]
    public void Migrate_FadeSameSceneBothSides_OneCopyPlayedTwice()
    {
        var scene = _project.CreateScene("Red");
        var fade = Add(new FadeFunction { FromSceneId = scene.Id, ToSceneId = scene.Id });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(fade.Id);
        Assert.Equal(chaser.Steps[0].FunctionId, chaser.Steps[1].FunctionId);
        Assert.Single(_project.OwnedFunctions(chaser.Id));
    }

    [Fact]
    public void Migrate_FadeOwningItsScene_ReusesItAsStep()
    {
        var fade = Add(new FadeFunction { DurationSeconds = 2 });
        var owned = Add(new SceneFunction { Name = "Own", OwnerFunctionId = fade.Id, ShowInFunctionList = false });
        fade.ToSceneId = owned.Id;

        LegacyFunctionMigration.Migrate(_project);

        Assert.Equal(owned.Id, Assert.Single(Chaser(fade.Id).Steps).FunctionId);
        Assert.Single(_project.OwnedFunctions(fade.Id));
    }

    // ───────────────────────────── Pulse ─────────────────────────────

    [Fact]
    public void Migrate_PulseWithColour_LoopingDarkBrightChaserOnDimmer()
    {
        var pulse = Add(new PulseFunction
        {
            Name = "Breathe",
            FixtureIds = [_par.Id, _bar.Id],
            Color = new RgbColor(255, 0, 0),
            PeriodSeconds = 4,
            MinLevel = 0.2,
            MaxLevel = 0.8,
        });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(pulse.Id);
        Assert.Equal("Breathe", chaser.Name);
        Assert.Equal(RunOrder.Loop, chaser.RunOrder);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.All(chaser.Steps, s => Assert.Equal((2.0, 0.0, 2.0), Times(s)));

        var dark = Step(chaser, 0);
        var bright = Step(chaser, 1);
        Assert.Equal(("Dark", "Bright"), (dark.Name, bright.Name));

        // With a Dimmer the level goes there and the colour stays full; without one the colour is scaled.
        Assert.Equal((byte?)51, dark.GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)255, dark.GetValue(_par.Id, "r"));
        Assert.Equal((byte?)204, bright.GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)51, dark.GetValue(_bar.Id, "r"));
        Assert.Equal((byte?)204, bright.GetValue(_bar.Id, "r"));
        Assert.Equal((byte?)0, bright.GetValue(_bar.Id, "g"));
    }

    [Fact]
    public void Migrate_PulseWithoutColour_OnlyDimmerChannels()
    {
        var pulse = Add(new PulseFunction { FixtureIds = [_par.Id, _bar.Id], Color = null, MinLevel = 0, MaxLevel = 1 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(pulse.Id);
        var dark = Step(chaser, 0);
        var bright = Step(chaser, 1);
        Assert.Equal([(_par.Id, "dim", (byte)0)], dark.Values.Select(v => (v.FixtureId, v.ChannelKey, v.Value)).ToArray());
        Assert.Equal([(_par.Id, "dim", (byte)255)], bright.Values.Select(v => (v.FixtureId, v.ChannelKey, v.Value)).ToArray());
    }

    [Fact]
    public void Migrate_PulseEqualLevels_OneSteadyStep()
    {
        var pulse = Add(new PulseFunction { FixtureIds = [_par.Id], MinLevel = 0.5, MaxLevel = 0.5, PeriodSeconds = 3 });

        LegacyFunctionMigration.Migrate(_project);

        var step = Assert.Single(Chaser(pulse.Id).Steps);
        Assert.Equal((0.0, 3.0, 0.0), Times(step));
        Assert.Equal((byte?)128, Step(Chaser(pulse.Id), 0).GetValue(_par.Id, "dim"));
    }

    [Fact]
    public void Migrate_PulseDamagedValues_UsesEngineDefaults()
    {
        var pulse = Add(new PulseFunction { FixtureIds = [_par.Id], PeriodSeconds = double.NaN, MinLevel = double.NaN, MaxLevel = 7 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(pulse.Id);
        Assert.All(chaser.Steps, s => Assert.Equal(PulseFunction.DefaultPeriodSeconds / 2, s.FadeInSeconds));
        Assert.Equal((byte?)0, Step(chaser, 0).GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)255, Step(chaser, 1).GetValue(_par.Id, "dim"));
    }

    // ───────────────────────────── Flash ─────────────────────────────

    [Fact]
    public void Migrate_Flash_BecomesSceneWithFlashColourAtFull()
    {
        var flash = Add(new FlashFunction { Name = "Hit", FixtureIds = [_par.Id, _bar.Id], Color = new RgbColor(0, 128, 255) });

        LegacyFunctionMigration.Migrate(_project);

        var scene = Assert.IsType<SceneFunction>(_project.FindFunction(flash.Id));
        Assert.Equal("Hit", scene.Name);
        Assert.Equal((0.0, 0.0), (scene.FadeInSeconds, scene.FadeOutSeconds));
        Assert.Equal((byte?)255, scene.GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)128, scene.GetValue(_par.Id, "g"));
        Assert.Equal((byte?)255, scene.GetValue(_par.Id, "b"));
        Assert.Equal((byte?)0, scene.GetValue(_par.Id, "r"));
        Assert.Equal((byte?)255, scene.GetValue(_bar.Id, "b"));
        Assert.Contains(scene, _project.FunctionListItems);
    }

    [Fact]
    public void Migrate_FlashWithDuration_SingleShotChaserHoldingForTheDuration()
    {
        var flash = Add(new FlashFunction { Name = "Blip", FixtureIds = [_par.Id], DurationSeconds = 0.2 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(flash.Id);
        Assert.Equal(RunOrder.SingleShot, chaser.RunOrder);
        Assert.Equal((0.0, 0.2, 0.0), Times(Assert.Single(chaser.Steps)));
        Assert.Equal((byte?)255, Step(chaser, 0).GetValue(_par.Id, "r"));
    }

    [Fact]
    public void Migrate_ConsoleButtonOnFlash_BecomesMomentaryFlashButton()
    {
        var flash = Add(new FlashFunction { Name = "Hit", FixtureIds = [_par.Id] });
        var timed = Add(new FlashFunction { Name = "Blip", FixtureIds = [_par.Id], DurationSeconds = 1 });
        var scene = _project.CreateScene("Look");
        var page = _project.CreateDefaultConsolePage();
        var flashButton = _project.AddFunctionButton(page.Id, flash.Id)!;
        var timedButton = _project.AddFunctionButton(page.Id, timed.Id)!;
        var sceneButton = _project.AddFunctionButton(page.Id, scene.Id)!;
        Assert.Equal(FunctionWidgetMode.Toggle, flashButton.Mode);

        LegacyFunctionMigration.Migrate(_project);

        Assert.Equal(FunctionWidgetMode.Flash, flashButton.Mode);
        Assert.Equal(FunctionWidgetMode.Flash, timedButton.Mode);
        Assert.Equal(FunctionWidgetMode.Toggle, sceneButton.Mode);
        Assert.IsType<SceneFunction>(_project.FindFunction(flashButton.FunctionId));
        Assert.Empty(_project.ValidateConsole());
    }

    [Fact]
    public void Deserialize_ConsoleButtonOnFlash_LoadsAsFlashButtonOnScene()
    {
        var flash = Add(new FlashFunction { Name = "Hit", FixtureIds = [_par.Id] });
        var page = _project.CreateDefaultConsolePage();
        var button = _project.AddFunctionButton(page.Id, flash.Id)!;

        var loaded = _serializer.Deserialize(_serializer.Serialize(_project)).Project;

        var loadedButton = Assert.IsType<FunctionWidget>(loaded.FindConsolePage(page.Id)!.FindWidget(button.Id));
        Assert.Equal(FunctionWidgetMode.Flash, loadedButton.Mode);
        Assert.IsType<SceneFunction>(loaded.FindFunction(flash.Id));
    }

    // ───────────────────────────── Strobe ─────────────────────────────

    [Fact]
    public void Migrate_HardwareStrobeWithValue_SceneSettingStrobeChannelWithLightOn()
    {
        var strobe = Add(new StrobeFunction
        {
            Name = "Strobe",
            FixtureIds = [_par.Id],
            Mode = StrobeMode.Hardware,
            HardwareValue = 180,
            Color = new RgbColor(10, 20, 30),
        });

        LegacyFunctionMigration.Migrate(_project);

        var scene = Assert.IsType<SceneFunction>(_project.FindFunction(strobe.Id));
        Assert.Equal("Strobe", scene.Name);
        Assert.Equal((byte?)180, scene.GetValue(_par.Id, "strobe"));
        Assert.Equal((byte?)255, scene.GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)10, scene.GetValue(_par.Id, "r"));
        Assert.Equal((byte?)30, scene.GetValue(_par.Id, "b"));
    }

    [Fact]
    public void Migrate_HardwareStrobeNoValue_UsesStartOfFirstStrobingRange()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_strobePar.Id], Mode = StrobeMode.Hardware });

        LegacyFunctionMigration.Migrate(_project);

        var scene = Assert.IsType<SceneFunction>(_project.FindFunction(strobe.Id));
        Assert.Equal((byte?)10, scene.GetValue(_strobePar.Id, "shutter"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeOnDocumentedStrobeChannel_OnOffChaserWithShutterOpen()
    {
        var strobe = Add(new StrobeFunction { Name = "Strobe", FixtureIds = [_strobePar.Id], RateHz = 5, DutyCycle = 0.5 });

        LegacyFunctionMigration.Migrate(_project);

        // Safety rule 8: never a fixed strobing value on the channel (not rate-limited by the engine); a chaser instead.
        var chaser = Chaser(strobe.Id);
        Assert.Equal("Strobe", chaser.Name);
        Assert.Equal(RunOrder.Loop, chaser.RunOrder);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.Equal([0.1, 0.1], chaser.Steps.Select(s => Math.Round(s.HoldSeconds, 9)));
        var on = Step(chaser, 0);
        var off = Step(chaser, 1);
        Assert.Equal((byte?)0, on.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)0, off.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)255, on.GetValue(_strobePar.Id, "dim"));
        Assert.Equal((byte?)0, off.GetValue(_strobePar.Id, "dim"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeFasterThanLimitOnStrobeChannel_ChaserCappedAndShutterNeverStrobing()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_strobePar.Id], RateHz = 25, DutyCycle = 0.05 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        Assert.True(chaser.Steps.Sum(s => s.HoldSeconds) >= 1 / LegacyFunctionMigration.MaxConvertedStrobeRateHz);
        Assert.All(chaser.Steps, s => Assert.True(s.HoldSeconds >= FunctionEditing.MinStepSeconds));
        AssertNotStrobing(Step(chaser, 0), _strobePar.Id, "shutter");
        AssertNotStrobing(Step(chaser, 1), _strobePar.Id, "shutter");
    }

    [Fact]
    public void Migrate_SoftwareStrobeOpenRangeNotAtDefault_ShutterHeldAtStartOfOpenRange()
    {
        var profile = ShutterProfile(
            "open-later",
            0,
            new() { Min = 0, Max = 9, Label = "Shutter closed" },
            new() { Min = 10, Max = 19, Label = "Shutter open" },
            new() { Min = 20, Max = 255, Label = "Strobe slow to fast" });
        var light = _project.AddFixtures(profile, "2ch", 1, 1, 300, "OPEN")[0];
        var strobe = Add(new StrobeFunction { FixtureIds = [light.Id], RateHz = 4 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        Assert.Equal((byte?)10, Step(chaser, 0).GetValue(light.Id, "shutter"));
        Assert.Equal((byte?)10, Step(chaser, 1).GetValue(light.Id, "shutter"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeDefaultStrobesNoOpenRange_ShutterHeldClosedNotStrobing()
    {
        var profile = ShutterProfile(
            "closed-only",
            100,
            new() { Min = 0, Max = 9, Label = "Blackout" },
            new() { Min = 10, Max = 255, Label = "Strobe slow to fast" });
        var light = _project.AddFixtures(profile, "2ch", 1, 1, 300, "CLOSED")[0];
        var strobe = Add(new StrobeFunction { FixtureIds = [light.Id], RateHz = 4 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        Assert.Equal((byte?)0, Step(chaser, 0).GetValue(light.Id, "shutter"));
        Assert.Equal((byte?)0, Step(chaser, 1).GetValue(light.Id, "shutter"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeUndocumentedStrobeChannel_ChaserLeavesChannelAlone()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_par.Id], RateHz = 4 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        Assert.Null(Step(chaser, 0).GetValue(_par.Id, "strobe"));
        Assert.Null(Step(chaser, 1).GetValue(_par.Id, "strobe"));
        Assert.Equal((byte?)0, Step(chaser, 1).GetValue(_par.Id, "dim"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeWithoutStrobeChannel_TwoStepOnOffChaser()
    {
        var strobe = Add(new StrobeFunction { Name = "Flicker", FixtureIds = [_bar.Id], RateHz = 5, DutyCycle = 0.3 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        Assert.Equal("Flicker", chaser.Name);
        Assert.Equal(RunOrder.Loop, chaser.RunOrder);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.Equal((0.0, 0.06, 0.0), (chaser.Steps[0].FadeInSeconds, Math.Round(chaser.Steps[0].HoldSeconds, 9), chaser.Steps[0].FadeOutSeconds));
        Assert.Equal((0.0, 0.14, 0.0), (chaser.Steps[1].FadeInSeconds, Math.Round(chaser.Steps[1].HoldSeconds, 9), chaser.Steps[1].FadeOutSeconds));
        var on = Step(chaser, 0);
        var off = Step(chaser, 1);
        Assert.Equal(("On", "Off"), (on.Name, off.Name));
        Assert.Equal((byte?)255, on.GetValue(_bar.Id, "r"));
        Assert.Equal((byte?)0, off.GetValue(_bar.Id, "r"));
        Assert.Equal((byte?)0, off.GetValue(_bar.Id, "w"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeTooFast_NeverFasterThanLimitOrOneStepPerHalf()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_bar.Id], RateHz = 25, DutyCycle = 0.05 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        var period = chaser.Steps.Sum(s => s.HoldSeconds);
        Assert.True(period >= 1 / LegacyFunctionMigration.MaxConvertedStrobeRateHz);
        Assert.All(chaser.Steps, s => Assert.True(s.HoldSeconds >= FunctionEditing.MinStepSeconds));
    }

    [Fact]
    public void Migrate_HardwareStrobeUndocumentedChannelNoValue_FallsBackToSoftwareChaser()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_par.Id], Mode = StrobeMode.Hardware, RateHz = 4 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        Assert.Null(Step(chaser, 0).GetValue(_par.Id, "strobe"));
        Assert.Equal((byte?)255, Step(chaser, 0).GetValue(_par.Id, "dim"));
        Assert.Equal((byte?)0, Step(chaser, 1).GetValue(_par.Id, "dim"));
    }

    [Fact]
    public void Migrate_SoftwareStrobeMixedLights_EveryLightFlashesInTheChaser()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_strobePar.Id, _bar.Id], RateHz = 5 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        var on = Step(chaser, 0);
        var off = Step(chaser, 1);
        Assert.Equal((byte?)0, on.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)0, off.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)255, on.GetValue(_strobePar.Id, "dim"));
        Assert.Equal((byte?)0, off.GetValue(_strobePar.Id, "dim"));
        Assert.Equal((byte?)255, on.GetValue(_bar.Id, "r"));
        Assert.Equal((byte?)0, off.GetValue(_bar.Id, "r"));
    }

    [Fact]
    public void Migrate_HardwareStrobeMixedLights_HardwareLightSteadyInBothSteps()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_strobePar.Id, _bar.Id], Mode = StrobeMode.Hardware, RateHz = 5 });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(strobe.Id);
        var on = Step(chaser, 0);
        var off = Step(chaser, 1);
        Assert.Equal((byte?)10, on.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)10, off.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)255, off.GetValue(_strobePar.Id, "dim"));
        Assert.Equal((byte?)0, off.GetValue(_bar.Id, "r"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    public void Migrate_SoftwareStrobeNoRate_SteadySceneWithShutterOpen(double rate)
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [_strobePar.Id, _bar.Id], RateHz = rate });

        LegacyFunctionMigration.Migrate(_project);

        var scene = Assert.IsType<SceneFunction>(_project.FindFunction(strobe.Id));
        Assert.Equal((byte?)0, scene.GetValue(_strobePar.Id, "shutter"));
        Assert.Equal((byte?)255, scene.GetValue(_bar.Id, "r"));
    }

    [Fact]
    public void Migrate_StrobeNoLights_EmptyScene()
    {
        var strobe = Add(new StrobeFunction { FixtureIds = [Guid.NewGuid()] });

        LegacyFunctionMigration.Migrate(_project);

        Assert.Empty(Assert.IsType<SceneFunction>(_project.FindFunction(strobe.Id)).Values);
    }

    // ───────────────────────────── References, ownership, nesting ─────────────────────────────

    [Fact]
    public void Migrate_ReferencesFromChaserShowAndConsole_StillResolve()
    {
        var pulse = Add(new PulseFunction { Name = "Pulse", FixtureIds = [_par.Id] });
        var strobe = Add(new StrobeFunction { Name = "Strobe", FixtureIds = [_bar.Id] });
        var chaser = Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(pulse.Id);
        var show = _project.CreateShow("Show");
        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, strobe.Id, 0, 4);
        var page = _project.CreateDefaultConsolePage();
        var button = _project.AddFunctionButton(page.Id, pulse.Id)!;

        LegacyFunctionMigration.Migrate(_project);

        Assert.IsType<ChaserFunction>(_project.FindFunction(chaser.Steps[0].FunctionId));
        Assert.Equal(pulse.Id, chaser.Steps[0].FunctionId);
        var showCopy = Assert.IsType<ChaserFunction>(_project.FindFunction(item.FunctionId));
        Assert.Equal(show.Id, showCopy.OwnerShowId);
        Assert.Equal(pulse.Id, button.FunctionId);
        Assert.Equal(FunctionWidgetMode.Toggle, button.Mode);
        Assert.Equal([chaser], _project.FunctionsReferencing(pulse.Id));
        Assert.Empty(_project.ValidateFunctionsAndShows());
        Assert.Empty(_project.ValidateConsole());
    }

    [Fact]
    public void Migrate_ChaserStepOwningPulseCopy_KeepsOwnershipAndNestsScenes()
    {
        var outer = Add(new ChaserFunction { Name = "Outer" });
        var pulse = Add(new PulseFunction { Name = "Pulse", FixtureIds = [_par.Id], OwnerFunctionId = outer.Id, ShowInFunctionList = false });
        outer.AddStep(pulse.Id);

        LegacyFunctionMigration.Migrate(_project);

        var inner = Chaser(pulse.Id);
        Assert.Equal(outer.Id, inner.OwnerFunctionId);
        Assert.False(inner.ShowInFunctionList);
        Assert.True(inner.IsHiddenFromList);
        var owned = _project.OwnedFunctions(outer.Id);
        Assert.Equal(3, owned.Count);
        Assert.Contains(Step(inner, 0), owned);
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void Migrate_ShowOwnedColourCycle_KeepsShowOwnershipAndListFlag()
    {
        var cycle = Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [_par.Id] });
        var show = _project.CreateShow("Show");
        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, cycle.Id, 0, 8);
        var copy = _project.FindFunction(item.FunctionId)!;
        copy.ShowInFunctionList = false;

        LegacyFunctionMigration.Migrate(_project);

        var converted = Chaser(item.FunctionId);
        Assert.Equal(show.Id, converted.OwnerShowId);
        Assert.Null(converted.OwnerFunctionId);
        Assert.False(converted.ShowInFunctionList);
        Assert.Equal(cycle.Id, converted.CopiedFromId);
        Assert.Equal(3, converted.Steps.Count);
        Assert.All(converted.Steps, s => Assert.Null(_project.FindFunction(s.FunctionId)!.OwnerShowId));
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void Migrate_FadeOfShowOwnedSceneCopies_CopiesTheirValues()
    {
        var show = _project.CreateShow("Show");
        var from = Add(new SceneFunction { Name = "From", OwnerShowId = show.Id, ShowInFunctionList = false });
        from.SetValue(_par.Id, "r", 10);
        var to = Add(new SceneFunction { Name = "To", OwnerShowId = show.Id, ShowInFunctionList = false });
        to.SetValue(_par.Id, "r", 250);
        var fade = Add(new FadeFunction { Name = "Fade", FromSceneId = from.Id, ToSceneId = to.Id, OwnerShowId = show.Id });

        LegacyFunctionMigration.Migrate(_project);

        var chaser = Chaser(fade.Id);
        Assert.Equal(show.Id, chaser.OwnerShowId);
        Assert.Equal((byte?)10, Step(chaser, 0).GetValue(_par.Id, "r"));
        Assert.Equal((byte?)250, Step(chaser, 1).GetValue(_par.Id, "r"));
        Assert.Equal(show.Id, from.OwnerShowId);
    }

    // ───────────────────────────── Idempotence / round trip / edge cases ─────────────────────────────

    [Fact]
    public void Migrate_Twice_SecondRunChangesNothing()
    {
        Add(new ColorCycleFunction { FixtureIds = [_par.Id] });
        Add(new StrobeFunction { FixtureIds = [_bar.Id] });
        Add(new StrobeFunction { FixtureIds = [_strobePar.Id, _bar.Id], RateHz = 8 });
        Add(new StrobeFunction { FixtureIds = [_strobePar.Id], Mode = StrobeMode.Hardware });
        Add(new FlashFunction { FixtureIds = [_par.Id] });
        var first = LegacyFunctionMigration.Migrate(_project);
        var snapshot = _serializer.Serialize(_project);

        var second = LegacyFunctionMigration.Migrate(_project);

        Assert.Equal(5, first);
        Assert.Equal(0, second);
        Assert.Equal(snapshot, _serializer.Serialize(_project));
    }

    [Fact]
    public void Deserialize_ProjectWithEveryRetiredType_SavesOnlyScenesAndChasersAndReloadsTheSame()
    {
        var red = _project.CreateScene("Red");
        red.SetValue(_par.Id, "r", 255);
        Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [_par.Id], FixtureOffsetSeconds = 0.5 });
        Add(new FadeFunction { Name = "Fade", ToSceneId = red.Id });
        Add(new PulseFunction { Name = "Pulse", FixtureIds = [_par.Id] });
        Add(new FlashFunction { Name = "Flash", FixtureIds = [_par.Id] });
        Add(new StrobeFunction { Name = "Strobe", FixtureIds = [_bar.Id] });

        var first = _serializer.Deserialize(_serializer.Serialize(_project));
        var saved = _serializer.Serialize(first.Project);
        var second = _serializer.Deserialize(saved);

        Assert.DoesNotContain(first.Project.Functions, LegacyFunctionMigration.IsLegacy);
        using (var json = System.Text.Json.JsonDocument.Parse(saved))
        {
            var types = json.RootElement.GetProperty("functions").EnumerateArray().Select(f => f.GetProperty("type").GetString()).Distinct().Order();
            Assert.Equal(["Chaser", "Scene"], types);
        }

        Assert.Equal(saved, _serializer.Serialize(second.Project));
        Assert.DoesNotContain(first.Issues, i => i.Code.StartsWith("FUNCTION", StringComparison.Ordinal) || i.Code.StartsWith("CHASER", StringComparison.Ordinal));
    }

    [Fact]
    public void Migrate_NoLegacyFunctions_ReturnsZero()
    {
        var empty = new Project();
        _project.CreateScene("Look");
        var count = _project.Functions.Count;

        Assert.Equal(0, LegacyFunctionMigration.Migrate(empty));
        Assert.Equal(0, LegacyFunctionMigration.Migrate(_project));
        Assert.Equal(count, _project.Functions.Count);
    }

    [Fact]
    public void Migrate_NullListsAndDamagedTimes_ConvertsSafely()
    {
        var cycle = Add(new ColorCycleFunction { FixtureIds = null!, HoldSeconds = double.NaN, FadeSeconds = -1 });
        var flash = Add(new FlashFunction { FixtureIds = null!, DurationSeconds = double.PositiveInfinity });
        var fade = Add(new FadeFunction { DurationSeconds = double.NaN });

        LegacyFunctionMigration.Migrate(_project);

        Assert.Empty(Chaser(cycle.Id).Steps);
        Assert.Empty(Assert.IsType<SceneFunction>(_project.FindFunction(flash.Id)).Values);
        Assert.Empty(Chaser(fade.Id).Steps);
    }

    [Fact]
    public void Migrate_DeletedAndDuplicateLights_SkippedOnce()
    {
        var gone = _project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 200, "GONE")[0];
        var flash = Add(new FlashFunction { FixtureIds = [_bar.Id, gone.Id, _bar.Id] });
        _project.RemoveFixture(gone.Id);

        LegacyFunctionMigration.Migrate(_project);

        var scene = Assert.IsType<SceneFunction>(_project.FindFunction(flash.Id));
        Assert.All(scene.Values, v => Assert.Equal(_bar.Id, v.FixtureId));
        Assert.Equal(4, scene.Values.Count);
    }

    [Fact]
    public void Migrate_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LegacyFunctionMigration.Migrate(null!));
    }
}
