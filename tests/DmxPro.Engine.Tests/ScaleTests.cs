using System.Diagnostics;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using Xunit.Abstractions;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Large-rig checks: 512 fixtures over 16 universes, five long shows and console effects over every fixture. The tick
/// must stay allocation-free and cheap, and recompiling after an edit must stay fast enough for the UI thread.
/// </summary>
public class ScaleTests
{
    private const int Universes = 16;
    private const int FixturesPerUniverse = 32;
    private const int ShowCount = 5;
    private const int ItemsPerTrack = 120;
    private const int TracksPerShow = 3;

    private readonly ITestOutputHelper _output;

    public ScaleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Tick_LargeRigWithFiveShowsAndConsoleEffects_AllocatesNothing()
    {
        var (engine, project, shows, effects) = BuildLargeRig();
        engine.LoadProject(project);
        engine.Tick(TimeSpan.Zero);
        engine.AllowStrobe = true;
        var sink = new CountingSink();
        engine.AddFrameSink(sink);
        foreach (var show in shows)
        {
            engine.StartShow(show, TimeSpan.FromSeconds(1));
        }

        engine.ShowOverrideMode = ShowOverrideMode.TouchedChannelsOnly;
        foreach (var effect in effects)
        {
            engine.StartFunction(effect, EngineLayer.Console, 0.6);
        }

        const int ticks = 400;
        var ticksRun = 0;
        var allocated = AllocationProbe.Measure(
            _ =>
            {
                engine.Tick(TestRig.Frame);
                ticksRun++;
            },
            iterations: ticks,
            warmupPerRound: 100);

        // Timing is a separate, loose pass: the allocation rounds above may have run more than one round of ticks.
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < ticks; i++)
        {
            engine.Tick(TestRig.Frame);
        }

        clock.Stop();
        ticksRun += ticks;
        _output.WriteLine($"Tick: {clock.Elapsed.TotalMilliseconds / ticks:0.000} ms average, {allocated} bytes allocated over {ticks} ticks");

        Assert.Equal(0, allocated);
        Assert.Equal(Universes * ticksRun, sink.Frames);
        Assert.Equal(ShowCount, engine.GetSnapshot()!.Shows.Count);

        // Generous bound (debug build, shared CI machine): a 44 Hz tick has ~22 ms; this catches O(n²) regressions only.
        Assert.True(clock.Elapsed.TotalMilliseconds / ticks < 10, $"Tick took {clock.Elapsed.TotalMilliseconds / ticks} ms");
    }

    [Fact]
    public void RefreshPatch_LargeRigWithShowCopies_SharesCompiledData()
    {
        var (engine, project, _, _) = BuildLargeRig();
        engine.LoadProject(project);
        engine.Tick(TimeSpan.Zero);

        // Warm-up (JIT), then the steady cost of one recompile after an edit.
        engine.RefreshPatch(project);
        engine.Tick(TimeSpan.Zero);
        const int runs = 5;
        var clock = new Stopwatch();
        var before = GC.GetAllocatedBytesForCurrentThread();
        clock.Start();
        for (var i = 0; i < runs; i++)
        {
            engine.RefreshPatch(project);
        }

        clock.Stop();
        var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
        engine.Tick(TimeSpan.Zero);
        _output.WriteLine(
            $"RefreshPatch: {clock.Elapsed.TotalMilliseconds / runs:0.0} ms average, {allocated / 1024} KiB allocated per refresh " +
            $"({project.Fixtures.Count} fixtures, {project.Functions.Count} functions)");

        // Allocation is deterministic, so it carries the regression check: show copies share their compiled arrays and
        // effect fixture lists (this rig allocated ~39 MB per refresh before that, ~4.4 MB after). Time only loosely.
        Assert.True(allocated < 12 * 1024 * 1024, $"RefreshPatch allocated {allocated / 1024} KiB");
        Assert.True(clock.Elapsed.TotalMilliseconds / runs < 2000, $"RefreshPatch took {clock.Elapsed.TotalMilliseconds / runs} ms");
    }

    [Fact]
    public void Tick_LargeRig_ShowsRenderSameAsBeforeScale()
    {
        // Correctness at scale: a show item lights its fixtures in the right universe.
        var (engine, project, shows, _) = BuildLargeRig();
        engine.SnapshotInterval = TimeSpan.Zero;
        engine.LoadProject(project);
        engine.StartShow(shows[0], TimeSpan.FromSeconds(0.5));
        engine.Tick(TimeSpan.Zero);

        // Item 0 of track 0 in show 0 is the "Red" scene copy: red of every fixture at 255 (fixtures at 1, 9, 17, …).
        var snapshot = engine.GetSnapshot()!;
        for (var universe = 1; universe <= Universes; universe++)
        {
            Assert.Equal(255, snapshot.GetValue(universe, 1));
            Assert.Equal(255, snapshot.GetValue(universe, 1 + (8 * (FixturesPerUniverse - 1))));
        }
    }

    private static (DmxEngine Engine, Project Project, Guid[] Shows, Guid[] Effects) BuildLargeRig()
    {
        var project = new Project();
        var profile = TestRig.Profile();
        var fixtures = new List<Guid>();
        for (var universe = 1; universe <= Universes; universe++)
        {
            foreach (var fixture in project.AddFixtures(profile, "8ch", FixturesPerUniverse, universe, 1, $"U{universe}"))
            {
                fixtures.Add(fixture.Id);
            }
        }

        var red = AllFixturesScene(project, fixtures, "Red", "r");
        var blue = AllFixturesScene(project, fixtures, "Blue", "b");
        var green = AllFixturesScene(project, fixtures, "Green", "g");
        var chaser = new ChaserFunction { Name = "Chaser" };
        chaser.AddStep(red.Id, 0.5, 0.1, 0.2);
        chaser.AddStep(blue.Id, 0.5, 0.1, 0.2);
        project.Functions.Add(chaser);

        var cycle = new ColorCycleFunction { Name = "Cycle", FixtureIds = [.. fixtures], FixtureOffsetSeconds = 0.01, HoldSeconds = 0.2, FadeSeconds = 0.3 };
        var pulse = new PulseFunction { Name = "Pulse", FixtureIds = [.. fixtures], PeriodSeconds = 0.7 };
        var strobe = new StrobeFunction { Name = "Strobe", FixtureIds = [.. fixtures.Take(64)], RateHz = 8 };
        project.Functions.Add(cycle);
        project.Functions.Add(pulse);
        project.Functions.Add(strobe);

        LightFunction[] sources = [red, blue, green, chaser, cycle, pulse];
        var shows = new Guid[ShowCount];
        for (var s = 0; s < ShowCount; s++)
        {
            var show = project.CreateShow($"Show {s + 1}");
            show.LengthSeconds = ItemsPerTrack + 10;
            for (var t = 1; t < TracksPerShow; t++)
            {
                project.AddTrack(show.Id, $"Track {t + 1}");
            }

            for (var t = 0; t < TracksPerShow; t++)
            {
                for (var i = 0; i < ItemsPerTrack; i++)
                {
                    var source = t == 0 ? sources[i % 3] : sources[(i + t) % sources.Length];
                    var item = project.PlaceFunctionInShow(show.Id, show.Tracks[t].Id, source.Id, i, 1);
                    item.SetTiming(i, 1, 0.1, 0.1);
                }
            }

            shows[s] = show.Id;
        }

        return (new DmxEngine(), project, shows, [chaser.Id, cycle.Id, pulse.Id, strobe.Id]);
    }

    private static SceneFunction AllFixturesScene(Project project, List<Guid> fixtures, string name, string key)
    {
        var scene = new SceneFunction { Name = name };
        foreach (var fixture in fixtures)
        {
            scene.SetValue(fixture, key, 255);
            scene.SetValue(fixture, "dim", 255);
        }

        project.Functions.Add(scene);
        return scene;
    }

    private sealed class CountingSink : IFrameSink
    {
        private readonly byte[] _buffer = new byte[DmxAddress.UniverseSize];

        public int Frames { get; private set; }

        public void OnFrame(int universe, ReadOnlySpan<byte> data)
        {
            data.CopyTo(_buffer);
            Frames++;
        }
    }
}
