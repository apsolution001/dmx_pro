using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Colour cycle, fade, pulse, flash and strobe were removed (owner decision 2026-10-28). Project load converts them, so
/// one reaching the engine is a leftover: it compiles to nothing and must never crash or light anything.
/// </summary>
public class LegacyFunctionTests
{
    public static TheoryData<string> LegacyTypes => ["ColorCycle", "Fade", "Pulse", "Flash", "Strobe"];

    private static LightFunction Legacy(string type, TestRig rig, Guid sceneId)
    {
        List<Guid> fixtures = [rig.Fixture.Id];
        var white = new RgbColor(255, 255, 255);
        LightFunction function = type switch
        {
            "ColorCycle" => new ColorCycleFunction { Name = type, FixtureIds = fixtures, Colors = [white] },
            "Fade" => new FadeFunction { Name = type, ToSceneId = sceneId, DurationSeconds = 1 },
            "Pulse" => new PulseFunction { Name = type, FixtureIds = fixtures, Color = white },
            "Flash" => new FlashFunction { Name = type, FixtureIds = fixtures, Color = white },
            "Strobe" => new StrobeFunction { Name = type, FixtureIds = fixtures, Color = white, Mode = StrobeMode.Hardware },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        rig.Project.Functions.Add(function);
        return function;
    }

    [Theory]
    [MemberData(nameof(LegacyTypes))]
    public void LoadProject_LegacyFunction_CompilesToNothing(string type)
    {
        var rig = new TestRig();
        var scene = rig.Scene("White", ("r", 255), ("g", 255), ("b", 255));
        var legacy = Legacy(type, rig, scene.Id);

        rig.Load();

        Assert.False(rig.Engine.CanPlay(legacy.Id));
        Assert.True(rig.Engine.CanPlay(scene.Id));
    }

    [Theory]
    [MemberData(nameof(LegacyTypes))]
    public void StartFunction_LegacyFunction_IgnoredAndOutputsDefaults(string type)
    {
        var rig = new TestRig();
        var scene = rig.Scene("White", ("r", 255), ("g", 255), ("b", 255));
        var legacy = Legacy(type, rig, scene.Id);
        rig.Load();
        rig.Engine.AllowStrobe = true;

        rig.Engine.StartFunction(legacy.Id, EngineLayer.Console);
        rig.Tick(0.5);

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Theory]
    [MemberData(nameof(LegacyTypes))]
    public void SetPreviewFunction_LegacyDraft_ReturnsFalseAndShowsNothing(string type)
    {
        var rig = new TestRig();
        var scene = rig.Scene("White", ("r", 255), ("g", 255), ("b", 255));
        var legacy = Legacy(type, rig, scene.Id);
        rig.Load();

        Assert.False(rig.Engine.SetPreviewFunction(rig.Project, PreviewDraftTests.DraftOf(legacy)));
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Theory]
    [MemberData(nameof(LegacyTypes))]
    public void StartFunction_ChaserStepPlaysLegacyFunction_StepShowsNothingOthersPlay(string type)
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var legacy = Legacy(type, rig, red.Id);
        var chaser = rig.Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(red.Id);
        chaser.AddStep(legacy.Id);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Tick(1); // 1.5 s: the legacy step keeps its time but lights nothing
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Theory]
    [MemberData(nameof(LegacyTypes))]
    public void StartShow_LegacyItem_SkippedWithoutError(string type)
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var legacy = Legacy(type, rig, red.Id);
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 10;
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, legacy.Id, 0, 5).SetTiming(0, 5);
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, red.Id, 5, 5).SetTiming(5, 5);
        rig.Load();
        rig.Engine.StartShow(show.Id);

        rig.Tick(1);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());

        rig.Tick(5); // 6 s
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void LoadProject_LegacyFunction_LogsThatItIsSkipped()
    {
        var logger = new ListLogger();
        var engine = new DmxEngine(logger);
        var project = new Project();
        project.Functions.Add(new PulseFunction { Name = "Old pulse" });

        engine.LoadProject(project);

        Assert.Contains(logger.Warnings, w => w.Contains("Old pulse", StringComparison.Ordinal));
    }

    private sealed class ListLogger : ILogger<DmxEngine>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
