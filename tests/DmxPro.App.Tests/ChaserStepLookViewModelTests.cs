using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>The chaser step-look panel: lights of one own-look step, colour presets, Off, level sliders, all channels.</summary>
public sealed class ChaserStepLookViewModelTests : IDisposable
{
    private readonly AppRig _rig = new();
    private readonly Project _project = new();
    private readonly Fixture _par1;
    private readonly Fixture _par2;
    private readonly Fixture _par3;

    public ChaserStepLookViewModelTests()
    {
        var fixtures = _project.AddFixtures(Rgbw(), "6ch", 3, 1, 1, "PAR");
        _par1 = fixtures[0];
        _par2 = fixtures[1];
        _par3 = fixtures[2];
    }

    public void Dispose() => _rig.Dispose();

    // ───────────────────────────── Opening ─────────────────────────────

    [Fact]
    public void Constructor_SceneWithValues_ShowsItsLightsAllSelected()
    {
        var vm = Create(SceneWith(_par2, _par1));

        Assert.Equal([_par1.Id, _par2.Id], vm.LightIds);
        Assert.All(vm.Lights, l => Assert.True(l.IsSelected));
        Assert.True(vm.HasLights);
        Assert.False(vm.DefaultLightsApplied);
        Assert.Equal(2, vm.Channels.Fixtures.Count);
        Assert.Contains(vm.Levels, r => r.Type == ChannelType.Dimmer);
        Assert.Contains(vm.Levels, r => r.Type == ChannelType.Red);
    }

    [Fact]
    public void Constructor_EmptySceneWithDefaults_AddsDefaultLightsAtProfileDefaults()
    {
        var scene = new SceneFunction();

        var vm = Create(scene, [_par1.Id, _par3.Id]);

        Assert.True(vm.DefaultLightsApplied);
        Assert.Equal([_par1.Id, _par3.Id], vm.LightIds);
        Assert.Equal((byte)255, scene.GetValue(_par3.Id, "dim"));
        Assert.Equal((byte)0, scene.GetValue(_par3.Id, "r"));
    }

    [Fact]
    public void Constructor_SceneWithValues_IgnoresDefaults()
    {
        var scene = SceneWith(_par2);

        var vm = Create(scene, [_par1.Id]);

        Assert.False(vm.DefaultLightsApplied);
        Assert.Equal([_par2.Id], vm.LightIds);
        Assert.Null(scene.GetValue(_par1.Id, "r"));
    }

    [Fact]
    public void Constructor_EmptySceneNoDefaults_ShowsEmptyState()
    {
        var vm = Create(new SceneFunction());

        Assert.True(vm.HasNoLights);
        Assert.Empty(vm.Levels);
        Assert.False(vm.ApplyPresetCommand.CanExecute(SceneEditorColorPreset.All[0]));
    }

    // ───────────────────────────── Colours ─────────────────────────────

    [Fact]
    public void ApplyPreset_DimmerAtZero_SetsColourRaisesDimmerAndRaisesChanged()
    {
        var scene = SceneWith(_par1, _par2);
        scene.SetValue(_par1.Id, "dim", 0);
        var vm = Create(scene);
        var changed = 0;
        vm.Changed += (_, _) => changed++;

        vm.ApplyPresetCommand.Execute(Preset("Blue"));

        Assert.Equal((byte)0, scene.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)255, scene.GetValue(_par1.Id, "b"));
        Assert.Equal((byte)255, scene.GetValue(_par1.Id, "dim"));
        Assert.Equal((byte)255, scene.GetValue(_par2.Id, "b"));
        Assert.Equal(1, changed);
        Assert.Equal("#0000FF", vm.Lights[0].SwatchHex);
    }

    [Fact]
    public void ApplyPreset_OnlySelectedLights()
    {
        var scene = SceneWith(_par1, _par2);
        var vm = Create(scene);
        vm.SelectNoLightsCommand.Execute(null);
        vm.Lights[1].IsSelected = true;

        vm.ApplyPresetCommand.Execute(Preset("Red"));

        Assert.Equal((byte)0, scene.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)255, scene.GetValue(_par2.Id, "r"));
    }

    [Fact]
    public void LightsOff_SetsColoursAndDimmerToZero()
    {
        var scene = SceneWith(_par1);
        scene.SetValue(_par1.Id, "r", 200);
        var vm = Create(scene);

        vm.LightsOffCommand.Execute(null);

        Assert.Equal((byte)0, scene.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)0, scene.GetValue(_par1.Id, "dim"));
        Assert.Equal(0, vm.Levels.Single(r => r.Type == ChannelType.Dimmer).Value);
    }

    [Fact]
    public void SelectNoLights_DisablesPresetsAndClearsLevels()
    {
        var vm = Create(SceneWith(_par1));

        vm.SelectNoLightsCommand.Execute(null);

        Assert.False(vm.ApplyPresetCommand.CanExecute(Preset("Red")));
        Assert.False(vm.LightsOffCommand.CanExecute(null));
        Assert.True(vm.HasNoSelectedLights);
        Assert.Empty(vm.Levels);
        Assert.Equal("0 of 1 selected", vm.SelectedLightsText);
    }

    [Fact]
    public void LevelSlider_WritesTypeToSelectedLightsAndFaders()
    {
        var scene = SceneWith(_par1, _par2);
        var vm = Create(scene);
        var changed = 0;
        vm.Changed += (_, _) => changed++;

        vm.Levels.Single(r => r.Type == ChannelType.Green).Value = 77;

        Assert.Equal((byte)77, scene.GetValue(_par1.Id, "g"));
        Assert.Equal((byte)77, scene.GetValue(_par2.Id, "g"));
        var fader = vm.Channels.Fixtures[0].Channels.Single(f => f.Type == ChannelType.Green);
        Assert.Equal((byte)77, fader.ByteValue);
        Assert.Equal(1, changed);
    }

    // ───────────────────────────── All channels ─────────────────────────────

    [Fact]
    public void AllChannels_FaderMove_WritesSceneUpdatesSliderAndRaisesChanged()
    {
        var scene = SceneWith(_par1);
        var vm = Create(scene);
        var changed = 0;
        vm.Changed += (_, _) => changed++;

        var fader = vm.Channels.Fixtures[0].Channels.Single(f => f.Type == ChannelType.Red);
        fader.Value = 150;

        Assert.Equal((byte)150, scene.GetValue(_par1.Id, "r"));
        Assert.Equal(150, vm.Levels.Single(r => r.Type == ChannelType.Red).Value);
        Assert.True(changed > 0);
    }

    [Fact]
    public void AllChannels_ExcludeChannel_RemovesValueFromScene()
    {
        var scene = SceneWith(_par1);
        var vm = Create(scene);

        vm.Channels.Fixtures[0].Channels.Single(f => f.Type == ChannelType.Strobe).IsIncluded = false;

        Assert.Null(scene.GetValue(_par1.Id, "strobe"));
        Assert.NotNull(scene.GetValue(_par1.Id, "r"));
    }

    // ───────────────────────────── Lights ─────────────────────────────

    [Fact]
    public void ChooseLights_AddsAndRemovesLights_NewOnesSelectedAtDefaults()
    {
        var scene = SceneWith(_par1, _par2);
        var vm = Create(scene);
        vm.SelectNoLightsCommand.Execute(null);
        vm.Lights[0].IsSelected = true;
        var changed = 0;
        vm.Changed += (_, _) => changed++;
        vm.ShowPicker = picker =>
        {
            picker.ClearCommand.Execute(null);
            picker.SelectAllCommand.Execute(null);
            picker.ApplyListSelection(Array.Empty<object>(), picker.VisibleLights.Where(l => l.Id == _par2.Id).ToList());
            return true;
        };

        vm.ChooseLightsCommand.Execute(null);

        Assert.Equal([_par1.Id, _par3.Id], vm.LightIds);
        Assert.Null(scene.GetValue(_par2.Id, "r"));
        Assert.Equal((byte)255, scene.GetValue(_par3.Id, "dim"));
        Assert.All(vm.Lights, l => Assert.True(l.IsSelected));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void ChooseLights_Cancelled_ChangesNothing()
    {
        var scene = SceneWith(_par1);
        var vm = Create(scene);
        var changed = 0;
        vm.Changed += (_, _) => changed++;
        vm.ShowPicker = picker =>
        {
            picker.SelectAllCommand.Execute(null);
            return false;
        };

        vm.ChooseLightsCommand.Execute(null);

        Assert.Equal([_par1.Id], vm.LightIds);
        Assert.Equal(0, changed);
    }

    // ───────────────────────────── Switching steps ─────────────────────────────

    [Fact]
    public void SetScene_KeepsSelectionOfLightsInBothSteps()
    {
        var vm = Create(SceneWith(_par1, _par2));
        vm.SelectNoLightsCommand.Execute(null);
        vm.Lights[1].IsSelected = true;
        vm.ValuesViewIndex = ChaserStepLookViewModel.AllChannelsView;
        var other = SceneWith(_par1, _par2, _par3);

        vm.SetScene(other);

        Assert.Same(other, vm.Scene);
        Assert.Equal([false, true, false], vm.Lights.Select(l => l.IsSelected));
        Assert.Equal(ChaserStepLookViewModel.AllChannelsView, vm.ValuesViewIndex);
    }

    [Fact]
    public void SetScene_NoCommonLights_SelectsAll()
    {
        var vm = Create(SceneWith(_par1));

        vm.SetScene(SceneWith(_par2, _par3));

        Assert.All(vm.Lights, l => Assert.True(l.IsSelected));
    }

    [Fact]
    public void SetScene_EmptySceneWithDefaults_AddsLightsAndRaisesChanged()
    {
        var vm = Create(SceneWith(_par1));
        var changed = 0;
        vm.Changed += (_, _) => changed++;
        var blank = new SceneFunction();

        vm.SetScene(blank, [_par2.Id]);

        Assert.True(vm.DefaultLightsApplied);
        Assert.Equal([_par2.Id], vm.LightIds);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void SetScene_EditsGoToTheNewSceneOnly()
    {
        var first = SceneWith(_par1);
        var vm = Create(first);
        var second = SceneWith(_par1);

        vm.SetScene(second);
        vm.ApplyPresetCommand.Execute(Preset("Green"));

        Assert.Equal((byte)255, second.GetValue(_par1.Id, "g"));
        Assert.Equal((byte)0, first.GetValue(_par1.Id, "g"));
    }

    // ───────────────────────────── Settings ─────────────────────────────

    [Fact]
    public void ShowStrobeBlockedHint_FollowsAllowStrobeSetting()
    {
        _rig.Settings.Update(s => s.AllowStrobe = false);
        var vm = Create(SceneWith(_par1));
        Assert.True(vm.ShowStrobeBlockedHint);

        _rig.Settings.Update(s => s.AllowStrobe = true);

        Assert.False(vm.ShowStrobeBlockedHint);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private ChaserStepLookViewModel Create(SceneFunction scene, IReadOnlyList<Guid>? defaults = null) =>
        new(_project, scene, _rig.Settings, _rig.Clipboard, defaults);

    private SceneFunction SceneWith(params Fixture[] fixtures)
    {
        var scene = new SceneFunction();
        foreach (var fixture in fixtures)
        {
            scene.AddFixture(_project, fixture);
        }

        return scene;
    }

    private static SceneEditorColorPreset Preset(string name) => SceneEditorColorPreset.All.Single(p => p.Name == name);

    private static FixtureProfile Rgbw() => new()
    {
        Id = "test-rgbw-dim",
        Manufacturer = "Test",
        Model = "RGBW",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
            new() { Key = "w", Name = "White", Type = ChannelType.White },
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer, Default = 255 },
            new() { Key = "strobe", Name = "Strobe", Type = ChannelType.Strobe },
        ],
        Modes =
        [
            new() { Name = "6ch", Channels = ["r", "g", "b", "w", "dim", "strobe"] },
        ],
    };
}
