namespace DmxPro.App.Tests;

public sealed class LiveModeServiceTests : IDisposable
{
    private readonly AppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void IsLive_NewService_IsOff()
    {
        Assert.False(_rig.LiveMode.IsLive);
        Assert.False(_rig.Engine.IsLiveMode);
    }

    [Fact]
    public void SetLive_On_TurnsEngineLiveAndRaisesEventOnce()
    {
        var raised = 0;
        _rig.LiveMode.LiveModeChanged += (_, _) => raised++;

        _rig.LiveMode.SetLive(true);
        _rig.LiveMode.SetLive(true);

        Assert.True(_rig.LiveMode.IsLive);
        Assert.True(_rig.Engine.IsLiveMode);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetLive_OffWhenAlreadyOff_RaisesNothing()
    {
        var raised = 0;
        _rig.LiveMode.LiveModeChanged += (_, _) => raised++;

        _rig.LiveMode.SetLive(false);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ProjectLoaded_WhileLive_TurnsLiveModeOffAndRaisesEvent()
    {
        _rig.StartEngine();
        _rig.LiveMode.SetLive(true);
        var raised = 0;
        _rig.LiveMode.LiveModeChanged += (_, _) => raised++;

        _rig.Projects.New();

        Assert.False(_rig.LiveMode.IsLive);
        Assert.False(_rig.Engine.IsLiveMode);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ProjectLoaded_WhileOff_RaisesNothing()
    {
        _rig.StartEngine();
        var raised = 0;
        _rig.LiveMode.LiveModeChanged += (_, _) => raised++;

        _rig.Projects.New();

        Assert.False(_rig.LiveMode.IsLive);
        Assert.Equal(0, raised);
    }
}
