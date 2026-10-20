using DmxPro.Core.Validation;
using DmxPro.Output.Configuration;
using DmxPro.Output.Transport;

namespace DmxPro.Output.Tests;

public class OutputManagerTests
{
    [Fact]
    public async Task NewManager_IsDisabled()
    {
        await using var manager = new OutputManager();

        Assert.False(manager.IsEnabled);
        Assert.Equal(OutputState.Disabled, manager.Status.State);
    }

    [Fact]
    public async Task EnableAsync_InvalidConfig_StartsNothing()
    {
        var factory = new RecordingFactory();
        await using var manager = new OutputManager(factory);
        await manager.SetConfigurationsAsync([new ArtNetOutputConfig { Destination = "nope" }]);

        var issues = await manager.EnableAsync();

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error);
        Assert.Empty(factory.Created);
        Assert.False(manager.IsEnabled);
        Assert.Equal(OutputState.Disabled, manager.Status.State);
    }

    [Fact]
    public async Task EnableAsync_NoConfigurations_ReturnsError()
    {
        await using var manager = new OutputManager();

        var issues = await manager.EnableAsync();

        Assert.Contains(issues, i => i.Code == "OUTPUT_NONE_CONFIGURED");
        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public async Task Submit_WhenEnabled_FansOutToEveryOutput()
    {
        var factory = new RecordingFactory();
        await using var manager = new OutputManager(factory);
        await manager.SetConfigurationsAsync(
        [
            new VirtualOutputConfig { Universes = [1, 2] },
            new VirtualOutputConfig { Universes = [2] },
        ]);
        await manager.EnableAsync();

        manager.Submit(2, TestFrames.Filled(42));
        foreach (var output in manager.ActiveOutputs)
        {
            Assert.True(await output.FlushAsync());
        }

        Assert.True(manager.IsEnabled);
        Assert.Equal(OutputState.Sending, manager.Status.State);
        foreach (var output in factory.Created.Cast<VirtualOutput>())
        {
            var frame = new byte[512];
            Assert.True(output.TryGetLastFrame(2, frame, out _));
            Assert.All(frame, b => Assert.Equal(42, b));
        }
    }

    [Fact]
    public async Task DisableAsync_StopsTransmissionWithoutSendingBlackout()
    {
        var factory = new RecordingFactory();
        await using var manager = new OutputManager(factory);
        await manager.SetConfigurationsAsync([new VirtualOutputConfig()]);
        await manager.EnableAsync();
        manager.Submit(1, TestFrames.Filled(200));
        await manager.ActiveOutputs[0].FlushAsync();
        var output = (VirtualOutput)factory.Created[0];

        await manager.DisableAsync();
        manager.Submit(1, TestFrames.Filled(7));

        Assert.False(manager.IsEnabled);
        Assert.Equal(OutputState.Disabled, manager.Status.State);
        Assert.Equal(OutputState.Disabled, output.Status.State);
        var last = new byte[512];
        Assert.True(output.TryGetLastFrame(1, last, out _));
        Assert.All(last, b => Assert.Equal(200, b)); // disabling is not blackout
    }

    [Fact]
    public async Task SendSafeFrameAndStopAsync_SendsSafeFrameThenStopsAndIgnoresLaterFrames()
    {
        var factory = new RecordingFactory();
        await using var manager = new OutputManager(factory);
        await manager.SetConfigurationsAsync([new VirtualOutputConfig { Universes = [1, 2] }]);
        await manager.EnableAsync();
        manager.Submit(1, TestFrames.Filled(255));
        manager.Submit(2, TestFrames.Filled(255));
        await manager.ActiveOutputs[0].FlushAsync();
        var output = (VirtualOutput)factory.Created[0];

        await manager.SendSafeFrameAndStopAsync(u => u == 1 ? TestFrames.Filled(0) : TestFrames.Filled(9));
        manager.Submit(1, TestFrames.Filled(255));

        Assert.False(manager.IsEnabled);
        Assert.Equal(OutputState.Disabled, output.Status.State);
        var frame = new byte[512];
        Assert.True(output.TryGetLastFrame(1, frame, out _));
        Assert.All(frame, b => Assert.Equal(0, b));
        Assert.True(output.TryGetLastFrame(2, frame, out _));
        Assert.All(frame, b => Assert.Equal(9, b));
        Assert.Contains(await manager.EnableAsync(), i => i.Code == "OUTPUT_CLOSING");
    }

    [Fact]
    public async Task Status_OneOutputFails_AggregateIsErrorWithOutputName()
    {
        var factory = new RecordingFactory
        {
            Serial = new FakeSerialPort { OpenFailure = () => new OutputTransportException("COM5 not found.") },
        };
        await using var manager = new OutputManager(factory);
        await manager.SetConfigurationsAsync(
        [
            new VirtualOutputConfig(),
            new EnttecProOutputConfig { Name = "USB", PortName = "COM5", Universes = [2] },
        ]);

        var issues = await manager.EnableAsync();

        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
        Assert.True(manager.IsEnabled);
        Assert.Equal(OutputState.Error, manager.Status.State);
        Assert.Equal("USB: COM5 not found.", manager.Status.Message);
    }

    [Fact]
    public async Task SetConfigurationsAsync_WhileEnabled_DisablesOutput()
    {
        await using var manager = new OutputManager();
        await manager.SetConfigurationsAsync([new VirtualOutputConfig()]);
        await manager.EnableAsync();

        await manager.SetConfigurationsAsync([new VirtualOutputConfig { Universes = [2] }]);

        Assert.False(manager.IsEnabled);
    }

    [Fact]
    public async Task Submit_WhenEnabled_AllocatesNothing()
    {
        await using var manager = new OutputManager();
        await manager.SetConfigurationsAsync([new VirtualOutputConfig(), new VirtualOutputConfig()]);
        await manager.EnableAsync();
        var a = TestFrames.Filled(1);
        var b = TestFrames.Filled(2);
        manager.Submit(1, a);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            manager.Submit(1, (i & 1) == 0 ? b : a);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task Submit_SixteenUniversesOverFourOutputs_AllocatesNothingAndDeliversEveryUniverse()
    {
        // A big rig: 16 universes split over four outputs, every universe changing every frame.
        await using var manager = new OutputManager();
        var outputs = new List<OutputConfig>();
        for (var o = 0; o < 4; o++)
        {
            outputs.Add(new VirtualOutputConfig { Universes = [(o * 4) + 1, (o * 4) + 2, (o * 4) + 3, (o * 4) + 4] });
        }

        await manager.SetConfigurationsAsync(outputs);
        await manager.EnableAsync();
        var a = TestFrames.Filled(1);
        var b = TestFrames.Filled(2);
        for (var universe = 1; universe <= 16; universe++)
        {
            manager.Submit(universe, a);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 200; frame++)
        {
            for (var universe = 1; universe <= 16; universe++)
            {
                manager.Submit(universe, (frame & 1) == 0 ? b : a);
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        // Every universe reaches its output's send thread.
        var buffer = new byte[512];
        foreach (var output in manager.ActiveOutputs.Cast<VirtualOutput>())
        {
            foreach (var universe in output.Universes)
            {
                Assert.True(await TestFrames.WaitUntilAsync(() => output.TryGetLastFrame(universe, buffer, out _)), $"universe {universe}");
            }
        }
    }

    private sealed class RecordingFactory : IDmxOutputFactory
    {
        public List<IDmxOutput> Created { get; } = [];

        public FakeSerialPort Serial { get; init; } = new();

        public IDmxOutput Create(OutputConfig config)
        {
            IDmxOutput output = config switch
            {
                EnttecProOutputConfig => new EnttecProOutput(Serial) { RetryDelay = TimeSpan.FromSeconds(30) },
                _ => new DmxOutputFactory().Create(config),
            };
            Created.Add(output);
            return output;
        }
    }
}
