using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>Fade editor: scene lists, search, swap, duration presets, explanation, notes and colour strip.</summary>
public sealed class FadeEditorViewModelTests
{
    private readonly Project _project = new();
    private readonly IReadOnlyList<Fixture> _pars;

    public FadeEditorViewModelTests()
    {
        _pars = _project.AddFixtures(Rgb(), "3ch", 5, 1, 1, "PAR");
    }

    // ───────────────────────────── Lists and empty state ─────────────────────────────

    [Fact]
    public void Create_NoScenes_ShowsEmptyState()
    {
        var vm = Create(Fade());

        Assert.True(vm.HasNoScenes);
        Assert.False(vm.HasScenes);
    }

    [Fact]
    public void Create_Scenes_FromListStartsWithCurrentLookAndToListHasOnlyScenes()
    {
        Scene("Blue", 0, 0, 255, 0, 1);
        Scene("Red", 255, 0, 0, 0, 1, 2);
        var vm = Create(Fade());

        Assert.Equal(["Current look", "Blue", "Red"], vm.From.Options.Select(o => o.Name));
        Assert.Equal(["Blue", "Red"], vm.To.Options.Select(o => o.Name));
        Assert.Equal("3 lights", vm.From.Options[2].Detail);
        Assert.Equal("#FF0000", vm.From.Options[2].Swatch);
    }

    [Fact]
    public void Create_ChaserOwnedScene_IsNotOffered()
    {
        Scene("Blue", 0, 0, 255, 0);
        Scene("Step 1", 255, 0, 0, 0).OwnerFunctionId = Guid.NewGuid();
        var vm = Create(Fade());

        Assert.Equal(["Blue"], vm.To.Options.Select(o => o.Name));
    }

    [Fact]
    public void Create_NoFromScene_ShowsCurrentLookAndToIsUnset()
    {
        Scene("Blue", 0, 0, 255, 0);
        var vm = Create(Fade());

        Assert.True(vm.From.IsCurrentLook);
        Assert.Equal("Current look", vm.From.DisplayName);
        Assert.True(vm.To.IsUnset);
        Assert.True(vm.To.NeedsAttention);
        Assert.True(vm.HasNoTarget);
        Assert.Equal("Pick a To scene: until then this fade does nothing.", vm.SummaryText);
    }

    [Fact]
    public void Create_ChosenScene_ShowsItsLightsAsDots()
    {
        var red = Scene("Red", 255, 0, 0, 0, 1);
        var vm = Create(Fade(to: red));

        Assert.Equal("Red", vm.To.DisplayName);
        Assert.Equal(["#FF0000", "#FF0000"], vm.To.Lights.Select(l => l.Color));
        Assert.Null(vm.To.MoreLightsText);
        Assert.Same(vm.To.Options.Single(), vm.To.SelectedOption);
    }

    [Fact]
    public void Create_DeletedScenes_StaySelectedAndWarn()
    {
        Scene("Blue", 0, 0, 255, 0);
        var vm = Create(Fade(from: Guid.NewGuid(), to: Guid.NewGuid()));

        Assert.True(vm.From.IsMissing);
        Assert.True(vm.To.IsMissing);
        Assert.Contains(vm.From.Options, o => o.IsMissing);
        Assert.Contains(vm.To.Options, o => o.IsMissing);
        Assert.Equal(2, vm.Notices.Count(n => n.IsWarning && n.Text.Contains("deleted", StringComparison.Ordinal)));
        Assert.False(vm.HasGradient);
    }

    // ───────────────────────────── Picking and search ─────────────────────────────

    [Fact]
    public void SelectOption_PicksSceneAndMarksDirty()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var vm = Create(Fade());

        vm.To.SelectedOption = vm.To.Options.Single();

        Assert.Equal(red.Id, vm.ToSceneId);
        Assert.True(vm.IsDirty);
        Assert.Equal("Red", vm.To.DisplayName);
    }

    [Fact]
    public void SelectOption_Null_KeepsChoice()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var vm = Create(Fade(to: red));

        vm.To.SelectedOption = null;

        Assert.Equal(red.Id, vm.ToSceneId);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Search_FiltersCaseInsensitiveAndKeepsChoiceWhenHidden()
    {
        var red = Scene("Warm red", 255, 0, 0, 0);
        Scene("Deep blue", 0, 0, 255, 0);
        var vm = Create(Fade(to: red));

        vm.To.SearchText = "BLUE";

        Assert.Equal(["Deep blue"], vm.To.Options.Select(o => o.Name));
        Assert.Equal(red.Id, vm.ToSceneId);
        Assert.Equal("Warm red", vm.To.SelectedOption?.Name);
        Assert.False(vm.To.HasNoMatches);
    }

    [Fact]
    public void Search_NoMatch_SaysSo()
    {
        Scene("Red", 255, 0, 0, 0);
        var vm = Create(Fade());

        vm.From.SearchText = " green ";

        Assert.Empty(vm.From.Options);
        Assert.True(vm.From.HasNoMatches);
        Assert.Equal("No scene matches \"green\".", vm.From.NoMatchText);
    }

    // ───────────────────────────── Swap ─────────────────────────────

    [Fact]
    public void Swap_TwoScenes_ExchangesThem()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var blue = Scene("Blue", 0, 0, 255, 0);
        var vm = Create(Fade(red, blue));

        Assert.True(vm.SwapCommand.CanExecute(null));
        vm.SwapCommand.Execute(null);

        Assert.Equal(blue.Id, vm.FromSceneId);
        Assert.Equal(red.Id, vm.ToSceneId);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Swap_CurrentLookOrUnsetOrSameScene_IsDisabled()
    {
        var red = Scene("Red", 255, 0, 0, 0);

        Assert.False(Create(Fade(to: red)).SwapCommand.CanExecute(null));
        Assert.False(Create(Fade(from: red)).SwapCommand.CanExecute(null));
        Assert.False(Create(Fade(red, red)).SwapCommand.CanExecute(null));
    }

    // ───────────────────────────── Duration ─────────────────────────────

    [Fact]
    public void Create_DefaultDuration_ThreeSecondPresetActive()
    {
        var vm = Create(Fade());

        Assert.Equal([1d, 3d, 5d, 10d], vm.DurationPresets.Select(p => p.Seconds));
        Assert.Equal(["3 s"], vm.DurationPresets.Where(p => p.IsActive).Select(p => p.Label));
    }

    [Fact]
    public void SetDuration_Preset_SetsSecondsAndMovesHighlight()
    {
        var vm = Create(Fade());

        vm.SetDurationCommand.Execute(vm.DurationPresets[3]);

        Assert.Equal(10, vm.DurationSeconds);
        Assert.True(vm.IsDirty);
        Assert.Equal([false, false, false, true], vm.DurationPresets.Select(p => p.IsActive));
    }

    [Fact]
    public void DurationSeconds_OtherValue_NoPresetActive()
    {
        var vm = Create(Fade());

        vm.DurationSeconds = 2.5;

        Assert.DoesNotContain(vm.DurationPresets, p => p.IsActive);
    }

    [Fact]
    public void PresetIsSelected_SetTrue_AppliesDurationAndMovesRadio()
    {
        var vm = Create(Fade());

        vm.DurationPresets[0].IsSelected = true;

        Assert.Equal(1, vm.DurationSeconds);
        Assert.Equal([true, false, false, false], vm.DurationPresets.Select(p => p.IsSelected));
    }

    [Fact]
    public void PresetIsSelected_SetFalse_KeepsDuration()
    {
        var vm = Create(Fade());

        vm.DurationPresets[1].IsSelected = false;

        Assert.Equal(3, vm.DurationSeconds);
        Assert.True(vm.DurationPresets[1].IsSelected);
    }

    // ───────────────────────────── Explanation ─────────────────────────────

    [Fact]
    public void SummaryText_TwoScenes_DescribesTheMove()
    {
        var vm = Create(Fade(Scene("Red", 255, 0, 0, 0), Scene("Blue", 0, 0, 255, 0)));

        Assert.Equal(
            "The lights start on “Red” and move smoothly to “Blue” over 3 s, then stay on “Blue” until the fade is stopped.",
            vm.SummaryText);
    }

    [Fact]
    public void SummaryText_CurrentLook_MovesFromWhatLightsShowNow()
    {
        var vm = Create(Fade(to: Scene("Blue", 0, 0, 255, 0)));

        Assert.StartsWith("Over 3 s the lights move from what they show now to “Blue”", vm.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryText_ZeroDuration_Jumps()
    {
        var vm = Create(Fade(Scene("Red", 255, 0, 0, 0), Scene("Blue", 0, 0, 255, 0)));

        vm.DurationSeconds = 0;

        Assert.StartsWith("Jumps straight to “Blue”", vm.SummaryText, StringComparison.Ordinal);
    }

    // ───────────────────────────── Notes ─────────────────────────────

    [Fact]
    public void Notices_SameLights_None()
    {
        var vm = Create(Fade(Scene("Red", 255, 0, 0, 0, 1), Scene("Blue", 0, 0, 255, 0, 1)));

        Assert.Empty(vm.Notices);
        Assert.False(vm.HasNotices);
    }

    [Fact]
    public void Notices_LightsOnlyInOneScene_NamedPerSide()
    {
        var vm = Create(Fade(Scene("Red", 255, 0, 0, 0, 1, 2), Scene("Blue", 0, 0, 255, 0, 1, 3, 4)));

        Assert.Equal(
            [
                "2 lights are only in To (PAR 4, PAR 5): they fade in from what they show now.",
                "1 light is only in From (PAR 3): it fades back to how it would look without this fade.",
            ],
            vm.Notices.Select(n => n.Text));
        Assert.DoesNotContain(vm.Notices, n => n.IsWarning);
    }

    [Fact]
    public void Notices_NoLightsInCommon_OneNote()
    {
        var vm = Create(Fade(Scene("Red", 255, 0, 0, 0), Scene("Blue", 0, 0, 255, 1)));

        var notice = Assert.Single(vm.Notices);
        Assert.StartsWith("“Red” and “Blue” use different lights", notice.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Notices_SameScene_SaysNothingChanges()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var vm = Create(Fade(red, red));

        Assert.Equal(["From and To are the same scene, so the lights do not change."], vm.Notices.Select(n => n.Text));
    }

    [Fact]
    public void Notices_EmptyToScene_Warns()
    {
        var empty = new SceneFunction { Name = "Empty" };
        _project.Functions.Add(empty);
        var vm = Create(Fade(to: empty));

        var notice = Assert.Single(vm.Notices);
        Assert.True(notice.IsWarning);
        Assert.Contains("has no lights yet", notice.Text, StringComparison.Ordinal);
        Assert.Equal("This scene has no lights yet.", vm.To.DisplayDetail);
    }

    [Theory]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B", "C" }, "A, B, C")]
    [InlineData(new[] { "A", "B", "C", "D", "E" }, "A, B, C and 2 more")]
    public void ListNames_ShortensLongLists(string[] names, string expected)
    {
        Assert.Equal(expected, FadeEditorViewModel.ListNames(names));
    }

    // ───────────────────────────── Colour strip ─────────────────────────────

    [Fact]
    public void Gradient_TwoScenes_FromColourToColour()
    {
        var vm = Create(Fade(Scene("Red", 255, 0, 0, 0), Scene("Blue", 0, 0, 255, 0)));

        Assert.True(vm.HasGradient);
        Assert.Equal("#FFFF0000", vm.GradientStart);
        Assert.Equal("#FF0000FF", vm.GradientEnd);
        Assert.Equal("Red", vm.GradientStartLabel);
        Assert.Equal("Blue", vm.GradientEndLabel);
    }

    [Fact]
    public void Gradient_CurrentLook_GrowsFromTransparent()
    {
        var vm = Create(Fade(to: Scene("Blue", 0, 0, 255, 0)));

        Assert.Equal("#000000FF", vm.GradientStart);
        Assert.Equal("#FF0000FF", vm.GradientEnd);
        Assert.Equal("Now", vm.GradientStartLabel);
    }

    // ───────────────────────────── Save / revert / preview ─────────────────────────────

    [Fact]
    public void Save_WritesChoicesAndDuration()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var blue = Scene("Blue", 0, 0, 255, 0);
        var fade = Fade(red, blue);
        var vm = Create(fade);

        vm.FromSceneId = Guid.Empty;
        vm.DurationSeconds = 5;
        vm.Save();

        Assert.Null(fade.FromSceneId);
        Assert.Equal(blue.Id, fade.ToSceneId);
        Assert.Equal(5, fade.DurationSeconds);
    }

    [Fact]
    public void Revert_RestoresSavedChoices()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var blue = Scene("Blue", 0, 0, 255, 0);
        var vm = Create(Fade(red, blue));

        vm.SwapCommand.Execute(null);
        vm.RevertCommand.Execute(null);

        Assert.Equal(red.Id, vm.FromSceneId);
        Assert.Equal(blue.Id, vm.ToSceneId);
        Assert.Equal("Red", vm.From.DisplayName);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void BuildPreviewFunction_LeavesOriginalUntouched()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var blue = Scene("Blue", 0, 0, 255, 0);
        var fade = Fade(red, blue);
        var vm = Create(fade);

        vm.SwapCommand.Execute(null);
        var preview = (FadeFunction)vm.BuildPreviewFunction();

        Assert.Equal(blue.Id, preview.FromSceneId);
        Assert.Equal(red.Id, fade.FromSceneId);
    }

    [Fact]
    public void RefreshPatch_SceneAddedAndRenamed_ListsFollow()
    {
        var red = Scene("Red", 255, 0, 0, 0);
        var vm = Create(Fade(to: red));

        red.Name = "Crimson";
        Scene("Amber", 255, 128, 0, 0);
        vm.RefreshPatch();

        Assert.Equal(["Amber", "Crimson"], vm.To.Options.Select(o => o.Name));
        Assert.Equal("Crimson", vm.To.DisplayName);
        Assert.Equal(red.Id, vm.ToSceneId);
        Assert.False(vm.IsDirty);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static FixtureProfile Rgb() => new()
    {
        Id = "test-rgb",
        Manufacturer = "Test",
        Model = "RGB",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
        ],
        Modes = [new() { Name = "3ch", Channels = ["r", "g", "b"] }],
    };

    private FadeEditorViewModel Create(FadeFunction fade) => new(_project, fade);

    private FadeFunction Fade(SceneFunction? from = null, SceneFunction? to = null) => Fade(from?.Id, to?.Id);

    private FadeFunction Fade(Guid? from, Guid? to)
    {
        var fade = new FadeFunction { Name = "Fade", FromSceneId = from, ToSceneId = to };
        _project.Functions.Add(fade);
        return fade;
    }

    /// <summary>A scene setting R/G/B on the given PARs (0-based indexes).</summary>
    private SceneFunction Scene(string name, byte r, byte g, byte b, params int[] pars)
    {
        var scene = new SceneFunction { Name = name };
        foreach (var index in pars)
        {
            var id = _pars[index].Id;
            scene.SetValue(id, "r", r);
            scene.SetValue(id, "g", g);
            scene.SetValue(id, "b", b);
        }

        _project.Functions.Add(scene);
        return scene;
    }
}
