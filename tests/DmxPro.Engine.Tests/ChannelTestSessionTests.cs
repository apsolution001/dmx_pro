using DmxPro.Engine.Testing;

namespace DmxPro.Engine.Tests;

public class ChannelTestSessionTests
{
    private static (TestRig Rig, ChannelTestSession Session) Create()
    {
        var rig = new TestRig();
        rig.Load();
        return (rig, new ChannelTestSession(rig.Engine));
    }

    [Fact]
    public void Set_PatchedChannel_OverridesOutput()
    {
        var (rig, session) = Create();

        Assert.True(session.Set(rig.Fixture.Id, "r", (byte)200));
        rig.Tick();

        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.True(session.IsOverridden(rig.Fixture.Id, "r"));
        Assert.True(session.TryGetValue(rig.Fixture.Id, "r", out var value));
        Assert.Equal(200, value);
        Assert.Equal(1, session.Count);
        Assert.True(session.IsAddressOverridden(1, TestRig.Red));
    }

    [Fact]
    public void Set_RunningScene_OverrideWins()
    {
        var (rig, session) = Create();
        var scene = rig.Scene("Red", ("r", 255));
        rig.RefreshAndApply();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);

        session.Set(rig.Fixture.Id, "r", (byte)10);
        rig.Tick(5);

        Assert.Equal(10, rig.At(TestRig.Red));
    }

    [Fact]
    public void Set_UnknownChannel_ReturnsFalse()
    {
        var (rig, session) = Create();

        Assert.False(session.Set(rig.Fixture.Id, "pan", (byte)1));
        Assert.False(session.Set(Guid.NewGuid(), "r", (byte)1));
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void Set_DisabledFixture_ReturnsFalse()
    {
        var rig = new TestRig();
        rig.Fixture.Enabled = false;
        rig.Load();
        var session = new ChannelTestSession(rig.Engine);

        Assert.False(session.Set(rig.Fixture.Id, "r", (byte)1));
    }

    [Theory]
    [InlineData(300.0, 255)]
    [InlineData(-5.0, 0)]
    [InlineData(127.5, 128)]
    [InlineData(double.NaN, 0)]
    public void Set_DoubleValue_IsRoundedAndClamped(double input, byte expected)
    {
        var (rig, session) = Create();

        session.Set(rig.Fixture.Id, "r", input);
        rig.Tick();

        Assert.Equal(expected, rig.At(TestRig.Red));
    }

    [Fact]
    public void Release_OneChannel_ReturnsToDefaultAndKeepsOthers()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        session.Set(rig.Fixture.Id, "dim", (byte)50);
        rig.Tick();

        Assert.True(session.Release(rig.Fixture.Id, "dim"));
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Dimmer));
        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.False(session.Release(rig.Fixture.Id, "dim"));
    }

    [Fact]
    public void ReleaseFixture_TwoFixtures_ReleasesOnlyThatFixture()
    {
        var (rig, session) = Create();
        var other = rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 1, 100, "Other")[0];
        rig.RefreshAndApply();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        session.Set(rig.Fixture.Id, "g", (byte)100);
        session.Set(other.Id, "r", (byte)50);

        Assert.Equal(2, session.ReleaseFixture(rig.Fixture.Id));
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
        Assert.Equal(50, rig.At(100));
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void ReleaseAll_ForeignOverride_LeavesItAlone()
    {
        var (rig, session) = Create();
        rig.Engine.SetChannelOverride(1, 300, 42);
        session.Set(rig.Fixture.Id, "r", (byte)200);
        session.Set(rig.Fixture.Id, "b", (byte)90);
        rig.Tick();

        Assert.Equal(2, session.ReleaseAll());
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Blue));
        Assert.Equal(42, rig.At(300));
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void Reapply_FixtureReaddressed_MovesOverride()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();

        rig.Fixture.StartAddress = 100;
        rig.Engine.RefreshPatch(rig.Project);
        session.Reapply();
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(200, rig.At(100));
        Assert.True(session.IsAddressOverridden(1, 100));
        Assert.False(session.IsAddressOverridden(1, TestRig.Red));
    }

    [Fact]
    public void Reapply_FixtureDeleted_DropsOverride()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();

        rig.Project.RemoveFixture(rig.Fixture.Id);
        rig.Engine.RefreshPatch(rig.Project);
        Assert.Equal(1, session.Reapply());
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void Reapply_FixtureDisabled_DropsOverride()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "dim", (byte)10);
        rig.Tick();

        rig.Fixture.Enabled = false;
        rig.Engine.RefreshPatch(rig.Project);
        session.Reapply();
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Dimmer));
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void Reapply_FixtureMovedToOtherUniverse_MovesOverride()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();

        rig.Fixture.Universe = 2;
        rig.Engine.RefreshPatch(rig.Project);
        session.Reapply();
        rig.Tick();

        Assert.Equal(200, rig.At(TestRig.Red, universe: 2));
        Assert.Equal(0, rig.At(TestRig.Red, universe: 1));
    }

    [Fact]
    public void Release_OverlapSharingAddress_ResendsRemainingValue()
    {
        var (rig, session) = Create();
        var twin = rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 1, TestRig.Red, "Twin")[0];
        rig.RefreshAndApply();
        session.Set(rig.Fixture.Id, "r", (byte)100);
        session.Set(twin.Id, "r", (byte)200);
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Red));

        session.Release(twin.Id, "r");
        rig.Tick();

        Assert.Equal(100, rig.At(TestRig.Red));
        Assert.True(session.IsAddressOverridden(1, TestRig.Red));

        session.Release(rig.Fixture.Id, "r");
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void Reset_AfterEngineReleaseAll_ForgetsWithoutSending()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();

        rig.Engine.ReleaseAll();
        Assert.Equal(1, session.Reset());
        rig.Tick();

        Assert.Equal(0, session.Count);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void Set_GrandMasterAndBlackout_StillApplied()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        session.Set(rig.Fixture.Id, "macro", (byte)77);

        rig.Engine.GrandMaster = 0.5;
        rig.Tick();
        Assert.Equal(100, rig.At(TestRig.Red));
        Assert.Equal(77, rig.At(TestRig.Macro));

        rig.Engine.Blackout = true;
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(77, rig.At(TestRig.Macro));
    }

    [Fact]
    public void Set_StrobeWhileNotAllowed_OutputsSafeValue()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "strobe", (byte)200);
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Strobe));

        rig.Engine.AllowStrobe = true;
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Strobe));
    }

    [Fact]
    public void Set_AfterDeactivate_NothingOverridden()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);

        Assert.Equal(1, session.Deactivate());
        Assert.False(session.Set(rig.Fixture.Id, "g", (byte)100));
        rig.Tick();

        Assert.Equal(0, session.Count);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());

        session.Activate();
        Assert.True(session.Set(rig.Fixture.Id, "g", (byte)100));
    }

    [Fact]
    public void Reapply_AfterEngineReleaseAll_ResendsNothing()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();

        rig.Engine.ReleaseAll();
        rig.Engine.RefreshPatch(rig.Project);
        session.Reapply();
        rig.Tick();

        Assert.Equal(0, session.Count);
        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void Sync_AfterEngineReleaseAll_ForgetsOlderKeepsNewer()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Engine.ReleaseAll();
        session.Set(rig.Fixture.Id, "g", (byte)100);

        rig.Tick();

        Assert.False(session.IsOverridden(rig.Fixture.Id, "r"));
        Assert.True(session.IsOverridden(rig.Fixture.Id, "g"));
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(100, rig.At(TestRig.Green));
    }

    [Fact]
    public void Sync_AfterProjectLoad_ForgetsEverything()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);

        rig.Load();

        Assert.Equal(1, session.Sync());
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void ReleaseFixture_SharedAddress_ResendsOtherValue()
    {
        var (rig, session) = Create();
        var twin = rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 1, TestRig.Red, "Twin")[0];
        rig.RefreshAndApply();
        session.Set(rig.Fixture.Id, "r", (byte)100);
        session.Set(twin.Id, "r", (byte)200);

        session.ReleaseFixture(twin.Id);
        rig.Tick();

        Assert.Equal(100, rig.At(TestRig.Red));
    }

    [Fact]
    public void Set_StaleAddressWithoutReapply_ClearsOldAddress()
    {
        var (rig, session) = Create();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();

        rig.Fixture.StartAddress = 100;
        rig.Engine.RefreshPatch(rig.Project);
        session.Set(rig.Fixture.Id, "r", (byte)150);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(150, rig.At(100));
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void Reapply_TwoEntriesMovedOntoOneAddress_LastTouchedWins()
    {
        var (rig, session) = Create();
        var other = rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 1, 100, "Other")[0];
        rig.RefreshAndApply();
        session.Set(other.Id, "r", (byte)50);
        session.Set(rig.Fixture.Id, "r", (byte)200);
        session.Set(other.Id, "r", (byte)60);

        rig.Fixture.StartAddress = 100;
        rig.Engine.RefreshPatch(rig.Project);
        session.Reapply();
        rig.Tick();

        Assert.Equal(60, rig.At(100));
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.True(session.IsShadowed(rig.Fixture.Id, "r"));
        Assert.False(session.IsShadowed(other.Id, "r"));
    }

    [Fact]
    public void Reapply_UniverseGone_DropsOverride()
    {
        var (rig, session) = Create();
        rig.Fixture.Universe = 2;
        rig.RefreshAndApply();
        session.Set(rig.Fixture.Id, "r", (byte)200);
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Red, universe: 2));

        rig.Project.RemoveFixture(rig.Fixture.Id);
        rig.Engine.RefreshPatch(rig.Project);
        Assert.Equal(1, session.Reapply());
        rig.Tick();

        Assert.Equal(0, session.Count);
        Assert.Equal([1], rig.Engine.GetSnapshot()!.Universes);
    }

    [Fact]
    public void ReleaseGeneration_ReleaseAllClearOverridesLoad_Increments()
    {
        var rig = new TestRig();
        var start = rig.Engine.ReleaseGeneration;

        rig.Engine.ReleaseAll();
        rig.Engine.ClearOverrides();
        rig.Engine.LoadProject(rig.Project);
        rig.Engine.RefreshPatch(rig.Project);

        Assert.Equal(start + 3, rig.Engine.ReleaseGeneration);
    }
}
