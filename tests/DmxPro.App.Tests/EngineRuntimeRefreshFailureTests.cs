using DmxPro.App.Services;
using DmxPro.Core.Functions;
using Wpf.Ui.Controls;

namespace DmxPro.App.Tests;

/// <summary>
/// Safety rules 4 + 10: when the engine can not take a project edit it keeps playing the previous model (a deleted
/// function keeps running), so the failure must be visible in the info bar until the next good refresh.
/// </summary>
public sealed class EngineRuntimeRefreshFailureTests : IDisposable
{
    private readonly SynchronizationContext? _previousContext;
    private readonly AppRig _rig = new();

    public EngineRuntimeRefreshFailureTests()
    {
        // Without a UI context the runtime refreshes synchronously on every edit (no dispatcher to coalesce on), so the
        // tests can assert right after MarkChanged.
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        _rig.StartEngine();
    }

    public void Dispose()
    {
        _rig.Dispose();
        SynchronizationContext.SetSynchronizationContext(_previousContext);
    }

    [Fact]
    public void RefreshNow_CompilerThrows_ShowsErrorMessageAndKeepsIt()
    {
        BreakProject();

        Assert.True(_rig.Runtime.HasRefreshFailure);
        Assert.Equal(EngineRuntimeService.RefreshFailedMessage, _rig.InfoBar.Message);
        Assert.Equal(InfoBarSeverity.Error, _rig.InfoBar.Severity);
        Assert.True(_rig.InfoBar.IsOpen);
    }

    [Fact]
    public void RefreshNow_GoodRefreshAfterFailure_ClearsMessage()
    {
        BreakProject();

        RepairProject();

        Assert.False(_rig.Runtime.HasRefreshFailure);
        Assert.Null(_rig.InfoBar.Message);
        Assert.False(_rig.InfoBar.IsOpen);
    }

    [Fact]
    public void RefreshNow_DeletedFunctionWhileFailing_EngineKeepsOldModelUntilGoodRefresh()
    {
        var scene = new SceneFunction { Name = "Look" };
        _rig.Projects.Current.Functions.Add(scene);
        _rig.Projects.MarkChanged();
        Assert.True(_rig.Engine.CanPlay(scene.Id));

        _rig.Projects.Current.Functions.Remove(scene);
        BreakProject();

        // The stale model still knows the deleted function: exactly why the failure has to be visible.
        Assert.True(_rig.Engine.CanPlay(scene.Id));
        Assert.Equal(EngineRuntimeService.RefreshFailedMessage, _rig.InfoBar.Message);

        RepairProject();

        Assert.False(_rig.Engine.CanPlay(scene.Id));
        Assert.Null(_rig.InfoBar.Message);
    }

    [Fact]
    public void RefreshNow_FailsAgainAfterUserClosedBar_ShowsMessageAgain()
    {
        BreakProject();
        _rig.InfoBar.IsOpen = false;

        _rig.Projects.MarkChanged();

        Assert.Equal(EngineRuntimeService.RefreshFailedMessage, _rig.InfoBar.Message);
        Assert.True(_rig.InfoBar.IsOpen);
    }

    [Fact]
    public void RefreshNow_GoodRefreshWithNewerMessage_KeepsNewerMessage()
    {
        BreakProject();
        _rig.InfoBar.Show(InfoBarSeverity.Error, "Nothing imported.");

        RepairProject();

        Assert.False(_rig.Runtime.HasRefreshFailure);
        Assert.Equal("Nothing imported.", _rig.InfoBar.Message);
    }

    [Fact]
    public void RefreshNow_GoodRefreshWithoutEarlierFailure_LeavesOtherMessageAlone()
    {
        _rig.InfoBar.Show(InfoBarSeverity.Warning, "Something else");

        _rig.Projects.MarkChanged();

        Assert.False(_rig.Runtime.HasRefreshFailure);
        Assert.Equal("Something else", _rig.InfoBar.Message);
    }

    [Fact]
    public void ProjectLoaded_AfterRefreshFailure_ClearsMessage()
    {
        BreakProject();

        _rig.Projects.New();

        Assert.False(_rig.Runtime.HasRefreshFailure);
        Assert.Null(_rig.InfoBar.Message);
    }

    [Fact]
    public void RefreshNow_PatchRefreshedHandlerThrows_ShowsNoEngineFailure()
    {
        _rig.Runtime.PatchRefreshed += (_, _) => throw new InvalidOperationException("listener bug");

        _rig.Projects.MarkChanged();

        Assert.False(_rig.Runtime.HasRefreshFailure);
        Assert.Null(_rig.InfoBar.Message);
    }

    /// <summary>A null fixture makes the engine model builder throw, like any compiler bug would.</summary>
    private void BreakProject()
    {
        _rig.Projects.Current.Fixtures.Add(null!);
        _rig.Projects.MarkChanged();
    }

    private void RepairProject()
    {
        _rig.Projects.Current.Fixtures.RemoveAll(f => f is null);
        _rig.Projects.MarkChanged();
    }
}
