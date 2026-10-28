using DmxPro.Output.Configuration;

namespace DmxPro.Output.Tests;

public class VirtualOutputTests
{
    [Fact]
    public async Task Status_BeforeStart_IsDisabled()
    {
        await using var output = new VirtualOutput();

        Assert.Equal(OutputState.Disabled, output.Status.State);
        Assert.Empty(output.Universes);
    }

    [Fact]
    public async Task Submit_AfterStart_FrameIsTransmitted()
    {
        await using var output = new VirtualOutput();
        await output.StartAsync(new VirtualOutputConfig { Universes = [1, 3] });
        var frame = TestFrames.Ramp(512, start: 7);

        output.Submit(3, frame);
        var flushed = await output.FlushAsync();

        Assert.True(flushed);
        Assert.Equal(OutputState.Sending, output.Status.State);
        var received = new byte[512];
        Assert.True(output.TryGetLastFrame(3, received, out var length));
        Assert.Equal(512, length);
        Assert.Equal(frame, received);
        Assert.False(output.TryGetLastFrame(1, received, out _)); // nothing submitted for universe 1 → nothing sent
    }

    [Fact]
    public async Task Submit_UnconfiguredUniverse_IsIgnored()
    {
        await using var output = new VirtualOutput();
        await output.StartAsync(new VirtualOutputConfig { Universes = [1] });

        output.Submit(2, TestFrames.Filled(255));
        await output.FlushAsync();

        Assert.False(output.TryGetLastFrame(2, new byte[512], out _));
    }

    [Fact]
    public async Task SendThread_UnchangedData_IsRefreshedContinuously()
    {
        await using var output = new VirtualOutput();
        await output.StartAsync(new VirtualOutputConfig { FrameRateHz = 44 });
        output.Submit(1, TestFrames.Filled(10));
        await output.FlushAsync();
        var before = output.FramesSent;

        Assert.True(await TestFrames.WaitUntilAsync(() => output.FramesSent >= before + 5));
    }

    [Fact]
    public async Task StopAsync_WhileSending_ReportsDisabledAndIgnoresSubmit()
    {
        await using var output = new VirtualOutput();
        var states = new List<OutputState>();
        output.StatusChanged += (_, s) =>
        {
            lock (states)
            {
                states.Add(s.State);
            }
        };
        await output.StartAsync(new VirtualOutputConfig());

        await output.StopAsync();
        output.Submit(1, TestFrames.Filled(1));

        Assert.Equal(OutputState.Disabled, output.Status.State);
        Assert.False(await output.FlushAsync());
        lock (states)
        {
            Assert.Equal([OutputState.Connecting, OutputState.Sending, OutputState.Disabled], states.Distinct());
        }
    }

    [Fact]
    public async Task StartAsync_InvalidConfig_StaysStoppedWithErrorAndDoesNotThrow()
    {
        await using var output = new VirtualOutput();

        await output.StartAsync(new VirtualOutputConfig { Universes = [] });
        output.Submit(1, TestFrames.Filled(1));

        Assert.Equal(OutputState.Error, output.Status.State);
        Assert.Contains("universe", output.Status.Message);
        Assert.Empty(output.Universes);
    }

    [Fact]
    public async Task StartAsync_WrongConfigType_ReportsError()
    {
        await using var output = new VirtualOutput();

        await output.StartAsync(new ArtNetOutputConfig());

        Assert.Equal(OutputState.Error, output.Status.State);
    }

    [Fact]
    public async Task Submit_Repeated_AllocatesNothing()
    {
        await using var output = new VirtualOutput();
        await output.StartAsync(new VirtualOutputConfig { Universes = [1, 2] });
        var a = TestFrames.Filled(1);
        var b = TestFrames.Filled(2);
        output.Submit(2, a);

        Assert.Equal(0, AllocationProbe.Measure(i => output.Submit(2, (i & 1) == 0 ? b : a)));
    }
}
