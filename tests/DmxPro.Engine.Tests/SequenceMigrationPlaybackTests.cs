using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Proves the engine plays a chaser made by <see cref="SequenceMigration"/> (steps pointing at hidden per-step scenes)
/// frame-for-frame like the original <see cref="SequenceFunction"/>, also after a save / load of an old file.
/// </summary>
public class SequenceMigrationPlaybackTests
{
    private const int Ticks = 600; // 15 s at 25 ms

    private enum Start
    {
        /// <summary>Started like a Virtual Console button (console layer) by the sequence's id.</summary>
        Console,

        /// <summary>Placed in a show after the migration (a show copy of the migrated chaser).</summary>
        ShowItem,

        /// <summary>
        /// A console widget and a show item point at the sequence before the old file is saved; the migrated run loads
        /// that file and plays what the widget targets.
        /// </summary>
        ConsoleWidgetFromFile,

        /// <summary>Like <see cref="ConsoleWidgetFromFile"/>, but the show (with its own sequence copy) is played.</summary>
        ShowItemFromFile,
    }

    private static List<byte[]> Render(Action<TestRig, SequenceFunction> build, bool migrated, Start start)
    {
        var rig = new TestRig();
        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 1, 30, "Par 2");
        var sequence = rig.Add(new SequenceFunction { Name = "Seq", FixtureIds = [rig.Fixture.Id] });
        build(rig, sequence);

        var project = rig.Project;
        Guid? showId = null;
        var playId = sequence.Id;
        if (start is Start.ConsoleWidgetFromFile or Start.ShowItemFromFile)
        {
            // Old file: the widget and the show copy are made while the function is still a sequence.
            var page = project.AddConsolePage("Main");
            project.AddConsoleWidget(page.Id, new FunctionWidget { FunctionId = sequence.Id, Label = "Seq" });
            var show = PlaceInShow(project, sequence.Id);
            Assert.IsType<SequenceFunction>(project.FindFunction(show.Tracks[0].Items[0].FunctionId));
            showId = start == Start.ShowItemFromFile ? show.Id : null;

            if (migrated)
            {
                // The real load path: serialize the old project and read it back (that migrates).
                var serializer = new ProjectSerializer();
                project = serializer.Deserialize(serializer.Serialize(project)).Project;
                Assert.DoesNotContain(project.Functions, f => f is SequenceFunction);
                playId = project.ConsolePages[0].Widgets.OfType<FunctionWidget>().Single().FunctionId;
                Assert.IsType<ChaserFunction>(project.FindFunction(playId));
                Assert.IsType<ChaserFunction>(project.FindFunction(project.Shows[0].Tracks[0].Items[0].FunctionId));
            }
        }
        else
        {
            if (migrated)
            {
                Assert.Equal(1, SequenceMigration.Migrate(project));
            }

            if (start == Start.ShowItem)
            {
                showId = PlaceInShow(project, playId).Id;
            }
        }

        rig.Engine.LoadProject(project);
        rig.Engine.Tick(TimeSpan.Zero);
        if (showId is { } id)
        {
            rig.Engine.StartShow(id);
        }
        else
        {
            rig.Engine.StartFunction(playId, EngineLayer.Console);
        }

        var frames = new List<byte[]>(Ticks);
        for (var i = 0; i < Ticks; i++)
        {
            rig.Tick();
            frames.Add(rig.Universe());
        }

        return frames;
    }

    private static Show PlaceInShow(Project project, Guid functionId)
    {
        var show = project.CreateShow("Show");
        show.LengthSeconds = 60;
        var item = project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, functionId, 0, 60);
        item.SetTiming(0, 60, 0, 0);
        return show;
    }

    private static void AssertSame(Action<TestRig, SequenceFunction> build, Start start = Start.Console)
    {
        var original = Render(build, migrated: false, start);
        var migrated = Render(build, migrated: true, start);
        Assert.Contains(original, f => !f.AsSpan().SequenceEqual(TestRig.DefaultsFrame())); // the test really plays something
        for (var i = 0; i < Ticks; i++)
        {
            Assert.True(original[i].AsSpan().SequenceEqual(migrated[i]), $"Frame {i} differs between sequence and migrated chaser.");
        }
    }

    private static void ThreeColourSteps(TestRig rig, SequenceFunction s)
    {
        var f = rig.Fixture.Id;
        var a = new SequenceStep { HoldSeconds = 1.5 };
        a.SetValue(f, "r", 200);
        a.SetValue(f, "macro", 40);
        var b = new SequenceStep { FadeInSeconds = 0.5, HoldSeconds = 1, FadeOutSeconds = 0.75 };
        b.SetValue(f, "g", 180);
        b.SetValue(f, "macro", 80);
        var c = new SequenceStep { FadeInSeconds = 1, HoldSeconds = 0.5, FadeOutSeconds = 1 };
        c.SetValue(f, "b", 90);
        c.SetValue(f, "dim", 120);
        s.Steps.AddRange([a, b, c]);
    }

    [Fact]
    public void Playback_MigratedSequence_LoopForward_FramesEqualOriginal() => AssertSame(ThreeColourSteps);

    [Fact]
    public void Playback_MigratedSequence_LoopBackward_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            ThreeColourSteps(rig, s);
            s.Direction = RunDirection.Backward;
        });

    [Fact]
    public void Playback_MigratedSequence_SingleShotForward_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            ThreeColourSteps(rig, s);
            s.RunOrder = RunOrder.SingleShot;
        });

    [Fact]
    public void Playback_MigratedSequence_SingleShotBackward_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            ThreeColourSteps(rig, s);
            s.RunOrder = RunOrder.SingleShot;
            s.Direction = RunDirection.Backward;
        });

    [Fact]
    public void Playback_MigratedSequence_PingPongForward_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            ThreeColourSteps(rig, s);
            s.RunOrder = RunOrder.PingPong;
        });

    [Fact]
    public void Playback_MigratedSequence_PingPongBackward_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            ThreeColourSteps(rig, s);
            s.RunOrder = RunOrder.PingPong;
            s.Direction = RunDirection.Backward;
        });

    [Fact]
    public void Playback_MigratedSequence_LongFadesAndOverlappingFadeOut_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            var f = rig.Fixture.Id;
            var a = new SequenceStep { FadeInSeconds = 2, HoldSeconds = 2.5, FadeOutSeconds = 3 };
            a.SetValue(f, "r", 255);
            a.SetValue(f, "w", 77);
            var b = new SequenceStep { FadeInSeconds = 1.25, HoldSeconds = 1.5, FadeOutSeconds = 2 };
            b.SetValue(f, "r", 30);
            b.SetValue(f, "b", 255);
            s.Steps.AddRange([a, b]);
        });

    [Fact]
    public void Playback_MigratedSequence_DifferentLightsPerStep_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            var first = rig.Fixture.Id;
            var second = rig.Project.Fixtures[1].Id;
            s.FixtureIds = [first, second];
            var a = new SequenceStep { FadeInSeconds = 0.5, HoldSeconds = 1, FadeOutSeconds = 0.5 };
            a.SetValue(first, "r", 255);
            var b = new SequenceStep { FadeInSeconds = 0.5, HoldSeconds = 1, FadeOutSeconds = 0.5 };
            b.SetValue(second, "g", 255);
            b.SetValue(second, "macro", 60);
            var c = new SequenceStep { HoldSeconds = 1 };
            c.SetValue(first, "b", 100);
            c.SetValue(second, "b", 200);
            s.Steps.AddRange([a, b, c]);
        });

    [Fact]
    public void Playback_MigratedSequence_StepWithoutValues_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            var a = new SequenceStep { FadeInSeconds = 0.5, HoldSeconds = 1, FadeOutSeconds = 0.5 };
            a.SetValue(rig.Fixture.Id, "r", 255);
            var empty = new SequenceStep { FadeInSeconds = 0.5, HoldSeconds = 1, FadeOutSeconds = 0.5 };
            var c = new SequenceStep { HoldSeconds = 1 };
            c.SetValue(rig.Fixture.Id, "g", 150);
            s.Steps.AddRange([a, empty, c]);
        });

    [Fact]
    public void Playback_MigratedSequence_NoSteps_FramesEqualOriginal()
    {
        var original = Render((_, _) => { }, migrated: false, Start.Console);
        var migrated = Render((_, _) => { }, migrated: true, Start.Console);
        Assert.Equal(original, migrated);
    }

    [Fact]
    public void Playback_MigratedSequence_ManySteps_FramesEqualOriginal() =>
        AssertSame((rig, s) =>
        {
            for (var i = 0; i < 40; i++)
            {
                var step = new SequenceStep
                {
                    FadeInSeconds = i % 3 * 0.05,
                    HoldSeconds = 0.1 + i % 4 * 0.05,
                    FadeOutSeconds = i % 5 * 0.04,
                };
                step.SetValue(rig.Fixture.Id, i % 2 == 0 ? "r" : "b", (byte)(i * 6 + 5));
                step.SetValue(rig.Fixture.Id, "macro", (byte)(i * 5));
                s.Steps.Add(step);
            }
        });

    [Fact]
    public void Playback_MigratedSequence_ViaVirtualConsole_FramesEqualOriginal() =>
        AssertSame(ThreeColourSteps, Start.Console);

    [Fact]
    public void Playback_MigratedSequence_ViaShowItem_FramesEqualOriginal() =>
        AssertSame(ThreeColourSteps, Start.ShowItem);

    [Fact]
    public void Playback_MigratedSequence_ViaShowItemPingPong_FramesEqualOriginal() =>
        AssertSame(
            (rig, s) =>
            {
                ThreeColourSteps(rig, s);
                s.RunOrder = RunOrder.PingPong;
            },
            Start.ShowItem);

    [Fact]
    public void Playback_MigratedSequence_ViaVirtualConsoleAndShowItem_StillPlays()
    {
        AssertSame(ThreeColourSteps, Start.ConsoleWidgetFromFile);
        AssertSame(ThreeColourSteps, Start.ShowItemFromFile);
    }

    [Fact]
    public void Playback_MigratedSequence_LoadedFromFileShowItemPingPongBackward_FramesEqualOriginal() =>
        AssertSame(
            (rig, s) =>
            {
                ThreeColourSteps(rig, s);
                s.RunOrder = RunOrder.PingPong;
                s.Direction = RunDirection.Backward;
            },
            Start.ShowItemFromFile);

    [Fact]
    public void Migrate_Sequence_KeepsIdRunOrderDirectionAndStepTiming()
    {
        var project = new Project();
        var sequence = new SequenceFunction { Name = "S", RunOrder = RunOrder.PingPong, Direction = RunDirection.Backward };
        sequence.Steps.Add(new SequenceStep { FadeInSeconds = 1, HoldSeconds = 2, FadeOutSeconds = 3 });
        project.Functions.Add(sequence);

        SequenceMigration.Migrate(project);

        var chaser = Assert.IsType<ChaserFunction>(project.FindFunction(sequence.Id));
        Assert.Equal(RunOrder.PingPong, chaser.RunOrder);
        Assert.Equal(RunDirection.Backward, chaser.Direction);
        var step = Assert.Single(chaser.Steps);
        Assert.Equal((1d, 2d, 3d), (step.FadeInSeconds, step.HoldSeconds, step.FadeOutSeconds));
        var scene = Assert.IsType<SceneFunction>(project.FindFunction(step.FunctionId));
        Assert.Equal((0d, 0d), (scene.FadeInSeconds, scene.FadeOutSeconds));
    }
}
