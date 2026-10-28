using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

public class AllocationTests
{
    [Fact]
    public void Tick_SteadyStateWithEverythingActive_AllocatesNothing()
    {
        var rig = new TestRig();
        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 20, 2, 1, "Bar");
        var show = rig.Scene("Show", ("r", 255), ("macro", 10));
        var console = rig.Scene("Console", ("g", 200), ("macro", 30));
        console.FadeInSeconds = 0.5;
        var preview = rig.Scene("Preview", ("b", 100));
        var timeline = rig.Project.CreateShow("Timeline");
        var item = rig.Project.PlaceFunctionInShow(timeline.Id, timeline.Tracks[0].Id, show.Id, 0, 120);
        item.SetTiming(0, 120, 1, 1);
        rig.Load();

        var sink = new CopyingSink();
        rig.Engine.AddFrameSink(sink);
        rig.Engine.StartShow(timeline.Id);
        rig.Engine.StartFunction(console.Id, EngineLayer.Console, 0.8);
        rig.Engine.StartPreview(preview.Id);
        rig.Engine.SetChannelOverride(1, 300, 50);
        rig.Engine.GrandMaster = 0.7;

        var allocated = MeasureSteadyState(i =>
        {
            rig.Engine.SetFunctionIntensity(console.Id, EngineLayer.Console, (i % 10) / 10.0);
            rig.Engine.Blackout = i % 100 == 0;
            rig.Tick();
        });

        Assert.Equal(0, allocated);
        Assert.True(sink.Frames > 0);
    }

    [Fact]
    public void Tick_ShowTransportAndConsoleOverride_AllocatesNothing()
    {
        var rig = new TestRig();
        var show = rig.Scene("Show", ("r", 255), ("macro", 10));
        var console = rig.Scene("Console", ("g", 200));
        var timeline = rig.Project.CreateShow("Timeline");
        timeline.LengthSeconds = 1000;
        var item = rig.Project.PlaceFunctionInShow(timeline.Id, timeline.Tracks[0].Id, show.Id, 0, 900);
        item.SetTiming(0, 900, 1, 1);
        rig.Load();
        rig.Engine.StartShow(timeline.Id);

        void Cycle(int i)
        {
            switch (i % 8)
            {
                case 0: rig.Engine.PauseShow(timeline.Id); break;
                case 1: rig.Engine.SeekShow(timeline.Id, TimeSpan.FromSeconds(i % 500)); break;
                case 2: rig.Engine.ResumeShow(timeline.Id); break;
                case 3: rig.Engine.StartFunction(console.Id, EngineLayer.Console); break;
                case 5: rig.Engine.StopFunction(console.Id, EngineLayer.Console); break;
                case 6: rig.Engine.ShowOverrideMode = (i / 8) % 2 == 0 ? ShowOverrideMode.MuteWholeShow : ShowOverrideMode.TouchedChannelsOnly; break;
            }

            rig.Tick();
            rig.Engine.TryGetShowPlayback(timeline.Id, out _, out _);
        }

        var allocated = MeasureSteadyState(Cycle);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Tick_EveryFunctionTypeInConsoleAndShow_AllocatesNothing()
    {
        var rig = new TestRig();
        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 10, 1, 100, "Bar");
        var red = rig.Scene("Red", ("r", 255), ("macro", 10));
        var blue = rig.Scene("Blue", ("b", 255), ("macro", 30));
        var chaser = rig.Add(new ChaserFunction { Name = "Chaser", RunOrder = RunOrder.PingPong });
        chaser.AddStep(red.Id, 0.3, 0.1, 0.2);
        chaser.AddStep(blue.Id, 0.2, 0.1, 0.3);
        chaser.AddStep(red.Id, 0.1);
        var sequence = rig.Add(new SequenceFunction { Name = "Sequence", FixtureIds = [rig.Fixture.Id] });
        for (var i = 0; i < 4; i++)
        {
            var step = new SequenceStep { FadeInSeconds = 0.05, HoldSeconds = 0.1, FadeOutSeconds = 0.1 };
            step.SetValue(rig.Fixture.Id, "g", (byte)(i * 60));
            sequence.Steps.Add(step);
        }

        // Steps far below every speed limit: always played stretched, re-timed whenever the limit changes.
        var fast = rig.Add(new ChaserFunction { Name = "Fast" });
        fast.AddStep(red.Id, 0.01);
        fast.AddStep(blue.Id, 0.01);
        var nested = rig.Add(new ChaserFunction { Name = "Nested" });
        nested.AddStep(chaser.Id, 1, 0, 0.5);
        nested.AddStep(fast.Id, 0.5);
        nested.AddStep(sequence.Id, 0.5);

        var timeline = rig.Project.CreateShow("Timeline");
        timeline.LengthSeconds = 1000;
        LightFunction[] all = [red, chaser, sequence, fast, nested];
        foreach (var function in all)
        {
            rig.Project.PlaceFunctionInShow(timeline.Id, timeline.Tracks[0].Id, function.Id, 0, 900);
        }

        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 10;
        rig.Engine.ShowOverrideMode = ShowOverrideMode.TouchedChannelsOnly;
        rig.Engine.StartShow(timeline.Id);
        foreach (var function in all)
        {
            rig.Engine.StartFunction(function.Id, EngineLayer.Console, 0.7);
        }

        void Cycle(int i)
        {
            rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 1 + (i % 3));
            rig.Engine.SetFunctionSpeed(fast.Id, EngineLayer.Console, 0.5 + (i % 4));
            rig.Engine.AllowStrobe = i % 50 < 40;
            rig.Engine.MaxStrobeRateHz = 2 + (i % 7);
            rig.Tick();
        }

        var allocated = MeasureSteadyState(Cycle);

        Assert.Equal(0, allocated);
        Assert.Equal(all.Length, rig.Engine.GetSnapshot()!.ActiveFunctions.Count);
    }

    private static long MeasureSteadyState(Action<int> cycle) =>
        AllocationProbe.Measure(cycle, iterations: 2000, warmupPerRound: 200);

    private sealed class CopyingSink : IFrameSink
    {
        private readonly byte[] _buffer = new byte[512];

        public int Frames { get; private set; }

        public void OnFrame(int universe, ReadOnlySpan<byte> data)
        {
            data.CopyTo(_buffer);
            Frames++;
        }
    }
}
