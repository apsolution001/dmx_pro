using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>Colour cycle editor: colour list editing, quick fills with undo, spread across lights and the preview.</summary>
public sealed class ColorCycleEditorViewModelTests
{
    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor Green = new(0, 255, 0);
    private static readonly RgbColor Blue = new(0, 0, 255);

    [Fact]
    public void Open_DefaultCycle_ShowsColoursWithNamesAndRound()
    {
        var vm = Create(lights: 0);

        Assert.Equal(["Red", "Green", "Blue"], vm.Colors.Select(c => c.Name));
        Assert.Same(vm.Colors[0], vm.SelectedColor);
        Assert.Contains("6 s", vm.RoundText, StringComparison.Ordinal);
        Assert.Equal(3, vm.PreviewSlots.Count);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AddPreset_InsertsAfterSelectionAndSelectsIt()
    {
        var vm = Create(lights: 0);
        vm.SelectedColor = vm.Colors[0];

        vm.AddPresetCommand.Execute(SceneEditorColorPreset.All.Single(p => p.Name == "Yellow"));

        Assert.Equal(["Red", "Yellow", "Green", "Blue"], vm.Colors.Select(c => c.Name));
        Assert.Equal("Yellow", vm.SelectedColor?.Name);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void DuplicateColor_CopiesSelectedAfterIt()
    {
        var vm = Create(lights: 0);
        vm.SelectedColor = vm.Colors[1];

        vm.DuplicateColorCommand.Execute(null);

        Assert.Equal(["Red", "Green", "Green", "Blue"], vm.Colors.Select(c => c.Name));
        Assert.Same(vm.Colors[2], vm.SelectedColor);
    }

    [Fact]
    public void MoveColorDown_KeepsSelectionOnMovedColour()
    {
        var vm = Create(lights: 0);
        vm.SelectedColor = vm.Colors[0];

        vm.MoveColorDownCommand.Execute(null);

        Assert.Equal(["Green", "Red", "Blue"], vm.Colors.Select(c => c.Name));
        Assert.Equal("Red", vm.SelectedColor?.Name);
        Assert.Equal(2, vm.SelectedColor?.Number);
    }

    [Fact]
    public void RemoveColor_Last_ShowsEmptyState()
    {
        var vm = Create(lights: 0, colors: [Red]);

        vm.RemoveColorCommand.Execute(null);

        Assert.True(vm.HasNoColors);
        Assert.Null(vm.SelectedColor);
        Assert.Empty(vm.PreviewSlots);
        Assert.Equal("Add at least one colour.", vm.RoundText);
    }

    [Fact]
    public void ApplyPalette_ReplacesColoursAndUndoBringsThemBack()
    {
        var vm = Create(lights: 0);
        var rainbow = ColorCyclePalette.All.Single(p => p.Name == "Rainbow");

        vm.ApplyPaletteCommand.Execute(rainbow);

        Assert.Equal(7, vm.Colors.Count);
        Assert.True(vm.CanUndoFill);
        Assert.Equal("Replaced 3 colours with Rainbow.", vm.FillMessage);

        vm.UndoFillCommand.Execute(null);

        Assert.Equal(["Red", "Green", "Blue"], vm.Colors.Select(c => c.Name));
        Assert.False(vm.CanUndoFill);
    }

    [Fact]
    public void ApplyPalette_ThenOtherEdit_ForgetsUndo()
    {
        var vm = Create(lights: 0);
        vm.ApplyPaletteCommand.Execute(ColorCyclePalette.All[1]);

        vm.RemoveColorCommand.Execute(null);

        Assert.False(vm.CanUndoFill);
    }

    [Fact]
    public void ApplyPalette_OnEmptyList_OffersNoUndo()
    {
        var vm = Create(lights: 0, colors: []);

        vm.ApplyPaletteCommand.Execute(ColorCyclePalette.All[2]);

        Assert.Equal(4, vm.Colors.Count);
        Assert.False(vm.CanUndoFill);
    }

    [Fact]
    public void SpreadPercent_Full_SetsOffsetToRoundPerLight()
    {
        var vm = Create(lights: 3);

        vm.SpreadPercent = 100;

        // 3 colours × 2 s = 6 s, 3 lights: 2 s between lights.
        Assert.Equal(2, vm.FixtureOffsetSeconds, 6);
        Assert.Equal(["#FF0000", "#0000FF", "#00FF00"], vm.PreviewLights.Select(l => l.Hex));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void SpreadPercent_FewerThanTwoLights_CannotSpread()
    {
        var vm = Create(lights: 1);

        vm.SpreadPercent = 50;

        Assert.False(vm.CanSpread);
        Assert.Equal(0, vm.FixtureOffsetSeconds);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Open_SavedOffset_ShowsMatchingSpread()
    {
        var vm = Create(lights: 4, offset: 0.75);

        // 6 s round, 4 lights: 0.75 s = 50 %.
        Assert.Equal(50, vm.SpreadPercent, 6);
        Assert.Equal(0.75, vm.FixtureOffsetSeconds);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void HoldSeconds_Changed_KeepsSpreadPercent()
    {
        var vm = Create(lights: 3);
        vm.SpreadPercent = 100;

        vm.HoldSeconds = 3;

        // 3 colours × 4 s = 12 s round, 3 lights: still 100 % = 4 s between lights.
        Assert.Equal(100, vm.SpreadPercent);
        Assert.Equal(4, vm.FixtureOffsetSeconds, 6);
    }

    [Fact]
    public void Save_WritesColoursTimesAndOffset()
    {
        var cycle = NewCycle(lights: 2, colors: [Red, Green]);
        var vm = new ColorCycleEditorViewModel(new Project(), cycle);
        vm.SelectedColor = vm.Colors[1];
        vm.AddPresetCommand.Execute(SceneEditorColorPreset.All.Single(p => p.Name == "Blue"));
        vm.FadeSeconds = 0;
        vm.SpreadPercent = 50;

        vm.Save();

        Assert.Equal([Red, Green, Blue], cycle.Colors);
        Assert.Equal(0, cycle.FadeSeconds);
        // 3 colours × 1 s = 3 s round, 2 lights, 50 %: 0.75 s.
        Assert.Equal(0.75, cycle.FixtureOffsetSeconds, 6);
    }

    [Fact]
    public void EditSelectedColour_UpdatesNameAndPreview()
    {
        var vm = Create(lights: 0);

        vm.ColorEditor.Hex = "#FF8000";

        Assert.Equal("Orange", vm.Colors[0].Name);
        Assert.Equal("#FF8000", vm.PreviewSlots[0].Hex);
        Assert.Equal("Change colour 1 · Orange", vm.SelectedColorTitle);
        Assert.True(vm.IsDirty);
    }

    private static ColorCycleEditorViewModel Create(int lights, RgbColor[]? colors = null, double offset = 0) =>
        new(new Project(), NewCycle(lights, colors, offset));

    private static ColorCycleFunction NewCycle(int lights, RgbColor[]? colors = null, double offset = 0)
    {
        var cycle = new ColorCycleFunction
        {
            Name = "Cycle",
            FixtureOffsetSeconds = offset,
            FixtureIds = Enumerable.Range(0, lights).Select(_ => Guid.NewGuid()).ToList(),
        };
        if (colors is not null)
        {
            cycle.Colors = [.. colors];
        }

        return cycle;
    }
}
