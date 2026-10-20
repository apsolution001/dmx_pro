using DmxPro.Core.Patching;

namespace DmxPro.Engine.Tests;

public class FrameOutputTests
{
    [Fact]
    public void Tick_WithSink_DeliversEveryUniverseEveryTick()
    {
        var rig = new TestRig();
        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 2, 1, "Second");
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();
        var sink = new RecordingSink();
        rig.Engine.AddFrameSink(sink);
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);

        rig.Tick();
        rig.Tick();

        Assert.Equal(4, sink.Calls);
        Assert.Equal(255, sink.Last[1][DmxAddress.ToIndex(TestRig.Red)]);
        Assert.Equal(255, sink.Last[2][DmxAddress.ToIndex(5)]);
    }

    [Fact]
    public void Tick_SinkThrows_OtherSinksStillReceiveAndTickSucceeds()
    {
        var rig = new TestRig();
        rig.Load();
        var good = new RecordingSink();
        rig.Engine.AddFrameSink(new ThrowingSink());
        rig.Engine.AddFrameSink(good);

        rig.Tick();

        Assert.Equal(1, good.Calls);
    }

    [Fact]
    public void RemoveFrameSink_Removed_ReceivesNothing()
    {
        var rig = new TestRig();
        rig.Load();
        var sink = new RecordingSink();
        rig.Engine.AddFrameSink(sink);
        rig.Engine.AddFrameSink(sink);

        rig.Tick();
        rig.Engine.RemoveFrameSink(sink);
        rig.Tick();

        Assert.Equal(1, sink.Calls);
    }

    [Fact]
    public void GetSnapshot_Throttled_PublishesAtInterval()
    {
        var rig = new TestRig();
        rig.Load();
        rig.Engine.SnapshotInterval = TimeSpan.FromSeconds(0.1);
        var first = rig.Engine.GetSnapshot()!.Sequence;

        rig.Tick(0.05);
        Assert.Equal(first, rig.Engine.GetSnapshot()!.Sequence);

        rig.Tick(0.05);
        Assert.True(rig.Engine.GetSnapshot()!.Sequence > first);
    }

    [Fact]
    public void GetSnapshot_BeforeFirstTick_IsNull()
    {
        Assert.Null(new DmxEngine().GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_Returned_IsNotChangedByLaterTicks()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();
        var before = rig.Engine.GetSnapshot()!;

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();
        rig.Tick();

        Assert.Equal(0, before.GetValue(1, TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    private sealed class RecordingSink : IFrameSink
    {
        public int Calls { get; private set; }

        public Dictionary<int, byte[]> Last { get; } = [];

        public void OnFrame(int universe, ReadOnlySpan<byte> data)
        {
            Calls++;
            Last[universe] = data.ToArray();
        }
    }

    private sealed class ThrowingSink : IFrameSink
    {
        public void OnFrame(int universe, ReadOnlySpan<byte> data) => throw new InvalidOperationException("boom");
    }
}
