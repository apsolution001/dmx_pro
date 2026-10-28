using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>Models of the non-scene function types: factory, save/load, copies, references and validation.</summary>
public class FunctionTypesTests
{
    private readonly ProjectSerializer _serializer = new();
    private readonly Project _project = new();
    private readonly Fixture _par;
    private readonly SceneFunction _red;
    private readonly SceneFunction _blue;

    public FunctionTypesTests()
    {
        _par = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
        _red = _project.CreateScene("Red");
        _red.SetValue(_par.Id, "r", 255);
        _blue = _project.CreateScene("Blue");
        _blue.SetValue(_par.Id, "b", 255);
    }

    // ───────────────────────────── Factory / library ─────────────────────────────

    [Theory]
    [InlineData(FunctionType.Scene, typeof(SceneFunction))]
    [InlineData(FunctionType.Chaser, typeof(ChaserFunction))]
    [InlineData(FunctionType.ColorCycle, typeof(ColorCycleFunction))]
    [InlineData(FunctionType.Fade, typeof(FadeFunction))]
    [InlineData(FunctionType.Pulse, typeof(PulseFunction))]
    [InlineData(FunctionType.Flash, typeof(FlashFunction))]
    [InlineData(FunctionType.Strobe, typeof(StrobeFunction))]
    public void Create_EveryType_ReturnsMatchingModel(FunctionType type, Type expected)
    {
        var function = FunctionFactory.Create(type);

        Assert.IsType(expected, function);
        Assert.Equal(type, function.Type);
    }

    [Fact]
    public void Create_Sequence_ReturnsChaser()
    {
        // Sequence was merged into Chaser; the enum value only remains for old files.
        Assert.IsType<ChaserFunction>(FunctionFactory.Create(FunctionType.Sequence));
        Assert.Equal("Chaser 1", _project.CreateFunction(FunctionType.Sequence).Name);
    }

    [Fact]
    public void CreateFunction_NoName_UsesTypeBaseNameAndNextNumber()
    {
        var first = _project.CreateFunction(FunctionType.ColorCycle);
        var second = _project.CreateFunction(FunctionType.ColorCycle);
        var chaser = _project.CreateFunction(FunctionType.Chaser, "  Party ");

        Assert.Equal("Color cycle 1", first.Name);
        Assert.Equal("Color cycle 2", second.Name);
        Assert.Equal("Party", chaser.Name);
        Assert.Null(first.OwnerShowId);
        Assert.Contains(chaser, _project.FunctionListItems);
    }

    [Fact]
    public void Defaults_NewEffects_AreSafeAndSensible()
    {
        var strobe = new StrobeFunction();
        var pulse = new PulseFunction();
        var cycle = new ColorCycleFunction();
        var chaser = new ChaserFunction();

        Assert.Equal(StrobeMode.Software, strobe.Mode);
        Assert.Equal(5.0, strobe.RateHz);
        Assert.Equal(0.5, strobe.DutyCycle);
        Assert.Null(strobe.HardwareValue);
        Assert.Equal(new RgbColor(255, 255, 255), pulse.Color);
        Assert.Equal(2.0, pulse.PeriodSeconds);
        Assert.Equal([new RgbColor(255, 0, 0), new RgbColor(0, 255, 0), new RgbColor(0, 0, 255)], cycle.Colors);
        Assert.Equal(RunOrder.Loop, chaser.RunOrder);
        Assert.Equal(RunDirection.Forward, chaser.Direction);
        Assert.Equal(0, new FlashFunction().DurationSeconds);
        Assert.Equal(3.0, new FadeFunction().DurationSeconds);
    }

    // ───────────────────────────── Save / load ─────────────────────────────

    [Fact]
    public void Deserialize_EveryFunctionType_RoundTripsAllSettings()
    {
        var chaser = Add(new ChaserFunction { Name = "Chase", RunOrder = RunOrder.PingPong, Direction = RunDirection.Backward });
        chaser.AddStep(_red.Id, 1.5, 0.25, 0.75);
        chaser.AddStep(_blue.Id);
        var sequence = Add(new SequenceFunction { Name = "Seq", FixtureIds = [_par.Id], RunOrder = RunOrder.SingleShot });
        var step = new SequenceStep { Name = "Hit", FadeInSeconds = 0.1, HoldSeconds = 0.2, FadeOutSeconds = 0.3 };
        step.SetValue(_par.Id, "g", 77);
        sequence.Steps.Add(step);
        var cycle = Add(new ColorCycleFunction
        {
            Name = "Rainbow",
            FixtureIds = [_par.Id],
            Colors = [new RgbColor(255, 128, 0), new RgbColor(1, 2, 3)],
            HoldSeconds = 2,
            FadeSeconds = 0.5,
            FixtureOffsetSeconds = 0.25,
            UseWhiteChannel = true,
        });
        var fade = Add(new FadeFunction { Name = "Sunrise", FromSceneId = _red.Id, ToSceneId = _blue.Id, DurationSeconds = 12 });
        var pulse = Add(new PulseFunction
        {
            Name = "Breathe",
            FixtureIds = [_par.Id],
            Color = null,
            Shape = PulseShape.Triangle,
            PeriodSeconds = 4,
            MinLevel = 0.1,
            MaxLevel = 0.9,
        });
        var flash = Add(new FlashFunction { Name = "Hit", FixtureIds = [_par.Id], Color = new RgbColor(0, 0, 255), DurationSeconds = 0.2 });
        var strobe = Add(new StrobeFunction
        {
            Name = "Strobe",
            FixtureIds = [_par.Id],
            Mode = StrobeMode.Hardware,
            RateHz = 8,
            DutyCycle = 0.3,
            Color = new RgbColor(10, 20, 30),
            HardwareValue = 180,
        });

        var loaded = _serializer.Deserialize(_serializer.Serialize(_project)).Project;

        var c = Assert.IsType<ChaserFunction>(loaded.FindFunction(chaser.Id));
        Assert.Equal(RunOrder.PingPong, c.RunOrder);
        Assert.Equal(RunDirection.Backward, c.Direction);
        Assert.Equal(2, c.Steps.Count);
        Assert.Equal((_red.Id, 0.25, 1.5, 0.75), (c.Steps[0].FunctionId, c.Steps[0].FadeInSeconds, c.Steps[0].HoldSeconds, c.Steps[0].FadeOutSeconds));
        Assert.Equal(ChaserStep.DefaultHoldSeconds, c.Steps[1].HoldSeconds);

        // A sequence loads as a chaser (Sequence merged into Chaser) with an own-look scene per step.
        var s = Assert.IsType<ChaserFunction>(loaded.FindFunction(sequence.Id));
        Assert.Equal(RunOrder.SingleShot, s.RunOrder);
        var loadedStep = Assert.Single(s.Steps);
        Assert.Equal((0.1, 0.2, 0.3), (loadedStep.FadeInSeconds, loadedStep.HoldSeconds, loadedStep.FadeOutSeconds));
        var stepScene = Assert.IsType<SceneFunction>(loaded.FindFunction(loadedStep.FunctionId));
        Assert.Equal("Hit", stepScene.Name);
        Assert.Equal(s.Id, stepScene.OwnerFunctionId);
        Assert.Equal((byte)77, stepScene.GetValue(_par.Id, "g"));

        var cc = Assert.IsType<ColorCycleFunction>(loaded.FindFunction(cycle.Id));
        Assert.Equal(cycle.Colors, cc.Colors);
        Assert.Equal((2.0, 0.5, 0.25, true), (cc.HoldSeconds, cc.FadeSeconds, cc.FixtureOffsetSeconds, cc.UseWhiteChannel));
        Assert.Equal([_par.Id], cc.FixtureIds);

        var f = Assert.IsType<FadeFunction>(loaded.FindFunction(fade.Id));
        Assert.Equal((_red.Id, _blue.Id, 12.0), (f.FromSceneId, f.ToSceneId, f.DurationSeconds));

        var p = Assert.IsType<PulseFunction>(loaded.FindFunction(pulse.Id));
        Assert.Null(p.Color);
        Assert.Equal((PulseShape.Triangle, 4.0, 0.1, 0.9), (p.Shape, p.PeriodSeconds, p.MinLevel, p.MaxLevel));

        var fl = Assert.IsType<FlashFunction>(loaded.FindFunction(flash.Id));
        Assert.Equal((new RgbColor(0, 0, 255), 0.2), (fl.Color, fl.DurationSeconds));

        var st = Assert.IsType<StrobeFunction>(loaded.FindFunction(strobe.Id));
        Assert.Equal((StrobeMode.Hardware, 8.0, 0.3, new RgbColor(10, 20, 30), (byte?)180), (st.Mode, st.RateHz, st.DutyCycle, st.Color, st.HardwareValue));

        Assert.Empty(loaded.ValidateFunctionsAndShows());
    }

    [Fact]
    public void Serialize_EffectFunctions_WritesTypeDiscriminatorAndHexColours()
    {
        Add(new FlashFunction { Name = "Hit", Color = new RgbColor(255, 128, 0) });
        Add(new ColorCycleFunction { Name = "Cycle", Colors = [new RgbColor(0, 255, 16)] });

        var json = _serializer.Serialize(_project);

        Assert.Contains("\"type\": \"Flash\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"ColorCycle\"", json, StringComparison.Ordinal);
        Assert.Contains("\"color\": \"#FF8000\"", json, StringComparison.Ordinal);
        Assert.Contains("\"#00FF10\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isLight", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_ColourNotHex_ThrowsFormatException()
    {
        var json = _serializer.Serialize(_project).Replace(
            "\"functions\": [",
            "\"functions\": [ { \"type\": \"Flash\", \"name\": \"Bad\", \"color\": { \"r\": 1 } },",
            StringComparison.Ordinal);

        Assert.Throws<Serialization.DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Theory]
    [InlineData("#FF8000", 255, 128, 0)]
    [InlineData("00ff10", 0, 255, 16)]
    [InlineData(" #0a0B0c ", 10, 11, 12)]
    public void TryParseHex_ValidText_ParsesComponents(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColor.TryParseHex(text, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#FF000000")]
    public void TryParseHex_InvalidText_ReturnsFalse(string? text)
    {
        Assert.False(RgbColor.TryParseHex(text, out _));
    }

    // ───────────────────────────── Copies ─────────────────────────────

    [Fact]
    public void DuplicateFunction_Chaser_CopiesStepsIndependently()
    {
        var chaser = Add(new ChaserFunction { Name = "Chase", RunOrder = RunOrder.SingleShot });
        chaser.AddStep(_red.Id, 2);

        var copy = Assert.IsType<ChaserFunction>(_project.DuplicateFunction(chaser.Id));
        copy.Steps[0].HoldSeconds = 9;
        copy.AddStep(_blue.Id);

        Assert.Equal("Chase copy", copy.Name);
        Assert.Equal(RunOrder.SingleShot, copy.RunOrder);
        Assert.Equal(2, chaser.Steps[0].HoldSeconds);
        Assert.Single(chaser.Steps);
        Assert.Equal(_red.Id, copy.Steps[0].FunctionId); // steps still reference the same scenes
    }

    [Fact]
    public void CloneAsNew_Sequence_DeepCopiesStepsAndValues()
    {
        var sequence = Add(new SequenceFunction { Name = "Seq", FixtureIds = [_par.Id] });
        var step = new SequenceStep();
        step.SetValue(_par.Id, "r", 10);
        sequence.Steps.Add(step);

        var copy = (SequenceFunction)sequence.CloneAsNew();
        copy.Steps[0].SetValue(_par.Id, "r", 99);
        copy.FixtureIds.Clear();

        Assert.Equal((byte)10, sequence.Steps[0].GetValue(_par.Id, "r"));
        Assert.Equal([_par.Id], sequence.FixtureIds);
    }

    [Fact]
    public void CloneAsNew_Effects_CopyEverySettingAndOwnLists()
    {
        var cycle = new ColorCycleFunction { FixtureIds = [_par.Id], HoldSeconds = 3, FadeSeconds = 4, FixtureOffsetSeconds = 0.5, UseWhiteChannel = true };
        var pulse = new PulseFunction { FixtureIds = [_par.Id], Color = null, Shape = PulseShape.Triangle, PeriodSeconds = 3, MinLevel = 0.2, MaxLevel = 0.4 };
        var flash = new FlashFunction { FixtureIds = [_par.Id], Color = new RgbColor(1, 2, 3), DurationSeconds = 1 };
        var strobe = new StrobeFunction { FixtureIds = [_par.Id], Mode = StrobeMode.Hardware, RateHz = 7, DutyCycle = 0.2, HardwareValue = 50 };
        var fade = new FadeFunction { FromSceneId = _red.Id, ToSceneId = _blue.Id, DurationSeconds = 7 };

        var cycleCopy = (ColorCycleFunction)cycle.CloneAsNew();
        var pulseCopy = (PulseFunction)pulse.CloneAsNew();
        var flashCopy = (FlashFunction)flash.CloneAsNew();
        var strobeCopy = (StrobeFunction)strobe.CloneAsNew();
        var fadeCopy = (FadeFunction)fade.CloneAsNew();
        cycleCopy.FixtureIds.Clear();
        cycleCopy.Colors.Clear();

        Assert.Equal([_par.Id], cycle.FixtureIds);
        Assert.Equal(3, cycle.Colors.Count);
        Assert.Equal((3.0, 4.0, 0.5, true), (cycleCopy.HoldSeconds, cycleCopy.FadeSeconds, cycleCopy.FixtureOffsetSeconds, cycleCopy.UseWhiteChannel));
        Assert.Equal(((RgbColor?)null, PulseShape.Triangle, 3.0, 0.2, 0.4), (pulseCopy.Color, pulseCopy.Shape, pulseCopy.PeriodSeconds, pulseCopy.MinLevel, pulseCopy.MaxLevel));
        Assert.Equal([_par.Id], pulseCopy.FixtureIds);
        Assert.Equal((new RgbColor(1, 2, 3), 1.0), (flashCopy.Color, flashCopy.DurationSeconds));
        Assert.Equal((StrobeMode.Hardware, 7.0, 0.2, (byte?)50), (strobeCopy.Mode, strobeCopy.RateHz, strobeCopy.DutyCycle, strobeCopy.HardwareValue));
        Assert.Equal((_red.Id, _blue.Id, 7.0), (fadeCopy.FromSceneId, fadeCopy.ToSceneId, fadeCopy.DurationSeconds));
        Assert.Equal(fade.Id, fadeCopy.CopiedFromId);
    }

    [Fact]
    public void PlaceFunctionInShow_Chaser_CopyIsIndependentOfLibraryChaser()
    {
        var chaser = Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(_red.Id);
        var show = _project.CreateShow("Show");

        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 0, 4);
        var copy = Assert.IsType<ChaserFunction>(_project.FindFunction(item.FunctionId));
        copy.AddStep(_blue.Id);

        Assert.Single(chaser.Steps);
        Assert.Equal(show.Id, copy.OwnerShowId);
    }

    // ───────────────────────────── References / validation ─────────────────────────────

    [Fact]
    public void FunctionsReferencing_SceneUsedByChaserAndFade_ReturnsBoth()
    {
        var chaser = Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(_red.Id);
        var fade = Add(new FadeFunction { Name = "Fade", ToSceneId = _red.Id });
        Add(new FadeFunction { Name = "Other", ToSceneId = _blue.Id });

        var users = _project.FunctionsReferencing(_red.Id);

        Assert.Equal([chaser, fade], users);
    }

    [Fact]
    public void ReferencedFixtureIds_EffectAndSequence_ListsDistinctFixtures()
    {
        var other = Guid.NewGuid();
        var sequence = new SequenceFunction { FixtureIds = [_par.Id] };
        var step = new SequenceStep();
        step.SetValue(other, "r", 1);
        step.SetValue(_par.Id, "g", 1);
        sequence.Steps.Add(step);

        Assert.Equal([_par.Id, other], sequence.ReferencedFixtureIds());
        Assert.Equal([_par.Id], new PulseFunction { FixtureIds = [_par.Id, _par.Id] }.ReferencedFixtureIds());
    }

    [Fact]
    public void ValidateFunctionsAndShows_ChaserStepMissing_Warns()
    {
        var chaser = Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(Guid.NewGuid());

        var issue = Assert.Single(_project.ValidateFunctionsAndShows());

        Assert.Equal("CHASER_MISSING_FUNCTION", issue.Code);
        Assert.Equal(chaser.Id, issue.EntityId);
    }

    [Fact]
    public void ValidateFunctionsAndShows_ChasersPlayingEachOther_WarnsForBoth()
    {
        var a = Add(new ChaserFunction { Name = "A" });
        var b = Add(new ChaserFunction { Name = "B" });
        a.AddStep(b.Id);
        b.AddStep(a.Id);

        var issues = _project.ValidateFunctionsAndShows();

        Assert.Equal(2, issues.Count(i => i.Code == "FUNCTION_REFERENCE_LOOP"));
    }

    [Fact]
    public void ValidateFunctionsAndShows_FadeTargetNotAScene_Warns()
    {
        var chaser = Add(new ChaserFunction { Name = "Chase" });
        Add(new FadeFunction { Name = "Fade", FromSceneId = Guid.NewGuid(), ToSceneId = chaser.Id });

        var issues = _project.ValidateFunctionsAndShows();

        Assert.Equal(2, issues.Count(i => i.Code == "FADE_MISSING_SCENE"));
    }

    [Fact]
    public void ValidateFunctionsAndShows_EffectFixtureDeleted_Warns()
    {
        var flash = Add(new FlashFunction { Name = "Flash", FixtureIds = [_par.Id] });
        _project.RemoveFixture(_par.Id);

        var issues = _project.ValidateFunctionsAndShows();

        Assert.Contains(issues, i => i.Code == "FUNCTION_MISSING_FIXTURE" && i.EntityId == flash.Id);
    }

    [Fact]
    public void ValidateFunctionsAndShows_SequenceUnknownChannel_Warns()
    {
        var sequence = Add(new SequenceFunction { Name = "Seq", FixtureIds = [_par.Id] });
        var step = new SequenceStep();
        step.SetValue(_par.Id, "nope", 1);
        sequence.Steps.Add(step);

        var issue = Assert.Single(_project.ValidateFunctionsAndShows());

        Assert.Equal("SEQUENCE_UNKNOWN_CHANNEL", issue.Code);
    }

    private T Add<T>(T function)
        where T : LightFunction
    {
        _project.Functions.Add(function);
        return function;
    }
}
