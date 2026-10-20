using System.Diagnostics;
using DmxPro.Core.Patching;

namespace DmxPro.Engine.Tests;

public class EngineHostTests
{
    [Fact]
    public void Start_Running_TicksOnBackgroundThread()
    {
        var rig = new TestRig();
        rig.Engine.LoadProject(rig.Project);
        var sink = new CountingSink();
        rig.Engine.AddFrameSink(sink);
        using var host = new EngineHost(rig.Engine);

        host.Start();
        var waited = Stopwatch.StartNew();
        while (sink.Frames < 3 && waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(10);
        }

        Assert.True(host.IsRunning);
        Assert.True(sink.Frames >= 3);
    }

    [Fact]
    public void Stop_BlackoutSafeFrame_ReleasesAndSendsDarkFrame()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Mix", ("r", 255), ("macro", 40));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        var sink = new CountingSink();
        rig.Engine.AddFrameSink(sink);
        var host = new EngineHost(rig.Engine);
        host.Start();

        host.Stop(SafeFrameMode.Blackout);

        Assert.False(host.IsRunning);
        Assert.Equal(0, sink.Last[DmxAddress.ToIndex(TestRig.Red)]);
        Assert.Equal(0, sink.Last[DmxAddress.ToIndex(TestRig.Dimmer)]);
        Assert.Equal(0, sink.Last[DmxAddress.ToIndex(TestRig.Macro)]); // released → default
    }

    [Fact]
    public void Stop_ProfileDefaultsSafeFrame_SendsDefaults()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        var sink = new CountingSink();
        rig.Engine.AddFrameSink(sink);
        var host = new EngineHost(rig.Engine);

        host.Stop();

        Assert.Equal(TestRig.DefaultsFrame(), sink.Last);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(5000)]
    public void Constructor_InvalidRate_Throws(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EngineHost(new DmxEngine(), tickRateHz: rate));
    }

    private sealed class CountingSink : IFrameSink
    {
        private int _frames;

        public int Frames => Volatile.Read(ref _frames);

        public byte[] Last { get; } = new byte[512];

        public void OnFrame(int universe, ReadOnlySpan<byte> data)
        {
            data.CopyTo(Last);
            Interlocked.Increment(ref _frames);
        }
    }
}
