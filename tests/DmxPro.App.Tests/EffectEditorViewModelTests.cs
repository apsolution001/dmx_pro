using DmxPro.App.Services;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmxPro.App.Tests;

/// <summary>
/// Pulse, flash and strobe editors: presets, plain-language choices, choice groups, colour presets and the strobe safety
/// notes (rule 8). The editors work on a draft; only Save writes the function.
/// </summary>
public sealed class EffectEditorViewModelTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"DmxPro.EffectEditorTests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try
        {
            File.Delete(_settingsPath);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
    }

    // ─────────────── Shared ───────────────

    [Fact]
    public void EffectChoiceGroup_Sync_ChecksOnlyTheMatchingChoice()
    {
        var applied = new List<double>();
        var group = new EffectChoiceGroup<double>([(1, "A", ""), (2, "B", "")], applied.Add, EffectEditorFormat.NearlyEqual);

        group.Sync(2);
        Assert.Equal("B", group.Selected?.Label);

        group.Sync(3);
        Assert.Null(group.Selected);
        Assert.Empty(applied);
    }

    [Fact]
    public void EffectChoice_SetSelectedTrue_AppliesValue_FalseIsIgnored()
    {
        var applied = new List<double>();
        var group = new EffectChoiceGroup<double>([(1, "A", ""), (2, "B", "")], applied.Add);

        group.Choices[1].IsSelected = true;
        group.Choices[0].IsSelected = false;

        Assert.Equal([2.0], applied);
    }

    [Fact]
    public void Editor_HasNoFixtures_FollowsTheLights()
    {
        var empty = new PulseEditorViewModel(new Project(), new PulseFunction());
        var withLights = new PulseEditorViewModel(new Project(), new PulseFunction { FixtureIds = [Guid.NewGuid()] });

        Assert.True(empty.HasNoFixtures);
        Assert.False(withLights.HasNoFixtures);
    }

    [Fact]
    public void EffectChoiceGroup_GroupName_SharedInsideOneGroup_DifferentBetweenGroups()
    {
        var first = new EffectChoiceGroup<double>([(1, "A", ""), (2, "B", "")], _ => { });
        var second = new EffectChoiceGroup<double>([(1, "A", ""), (2, "B", "")], _ => { });

        Assert.False(string.IsNullOrEmpty(first.GroupName));
        Assert.All(first.Choices, c => Assert.Equal(first.GroupName, c.GroupName));
        Assert.NotEqual(first.GroupName, second.GroupName);
    }

    [Fact]
    public void ColorEditor_PresetColour_IsNamedAndMarked()
    {
        var colors = new ColorEditorViewModel(_ => { });

        colors.SetColorSilently(new RgbColor(255, 0, 0));

        Assert.Equal("Red", colors.ColorName);
        Assert.False(colors.IsCustomColor);
        Assert.Single(colors.PresetItems, p => p.IsSelected);
    }

    [Fact]
    public void ColorEditor_CustomColour_OpensCustomPart()
    {
        var colors = new ColorEditorViewModel(_ => { }) { IsCustomOpen = false };

        colors.SetColorSilently(new RgbColor(12, 34, 56));

        Assert.True(colors.IsCustomColor);
        Assert.True(colors.IsCustomOpen);
        Assert.Equal("Custom colour", colors.ColorName);
    }

    [Fact]
    public void ColorEditor_ApplyPresetItem_ReportsTheColour()
    {
        RgbColor? reported = null;
        var colors = new ColorEditorViewModel(c => reported = c);

        colors.ApplyPresetItemCommand.Execute(colors.PresetItems.First(p => p.Name == "Blue"));

        Assert.Equal(new RgbColor(0, 0, 255), reported);
        Assert.Equal("Blue", colors.ColorName);
    }

    // ─────────────── Pulse ───────────────

    [Fact]
    public void Pulse_DefaultPeriod_SelectsMedium()
    {
        var vm = new PulseEditorViewModel(new Project(), new PulseFunction());

        Assert.Equal("Medium", vm.SpeedPresets.Selected?.Label);
        Assert.Equal("Smooth", vm.ShapeChoices.Selected?.Label);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Pulse_ChooseSlow_SetsFourSecondsAndMarksDirty()
    {
        var pulse = new PulseFunction();
        var vm = new PulseEditorViewModel(new Project(), pulse);

        vm.SpeedPresets.Choices[0].IsSelected = true;

        Assert.Equal(PulseEditorViewModel.SlowPeriodSeconds, vm.PeriodSeconds);
        Assert.Equal("Slow", vm.SpeedPresets.Selected?.Label);
        Assert.True(vm.IsDirty);
        Assert.Equal(PulseFunction.DefaultPeriodSeconds, pulse.PeriodSeconds);

        vm.Save();
        Assert.Equal(PulseEditorViewModel.SlowPeriodSeconds, pulse.PeriodSeconds);
    }

    [Fact]
    public void Pulse_ExactPeriod_MatchingNoPreset_UnchecksAll()
    {
        var vm = new PulseEditorViewModel(new Project(), new PulseFunction());

        vm.PeriodSeconds = 3;

        Assert.Null(vm.SpeedPresets.Selected);
        Assert.Equal("= 20 times a minute", vm.SpeedText);
    }

    [Fact]
    public void Pulse_ChooseStraightShape_SetsTriangle()
    {
        var vm = new PulseEditorViewModel(new Project(), new PulseFunction());

        vm.ShapeChoices.Choices[1].IsSelected = true;

        Assert.Equal(PulseShape.Triangle, vm.Shape);
        Assert.False(vm.ShapeChoices.Choices[0].IsSelected);
    }

    [Fact]
    public void Pulse_BrightnessOnly_SavesNoColour()
    {
        var pulse = new PulseFunction();
        var vm = new PulseEditorViewModel(new Project(), pulse);

        vm.UsesColor = false;
        vm.Save();

        Assert.True(vm.DimmerOnly);
        Assert.Null(pulse.Color);
    }

    [Fact]
    public void Pulse_VeryFastBreath_PreviewIsStill()
    {
        var vm = new PulseEditorViewModel(new Project(), new PulseFunction());

        vm.PeriodSeconds = 0.2;

        Assert.False(vm.IsPreviewAnimated);
        Assert.Contains("Too fast to animate", vm.PreviewCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Pulse_LevelsFollowTheDraft_InPreview()
    {
        var vm = new PulseEditorViewModel(new Project(), new PulseFunction());

        vm.MaxLevelPercent = 60;

        Assert.Equal(0.6, EffectPreviewMath.PeakLevel(vm.PreviewPoints), 2);
    }

    // ─────────────── Flash ───────────────

    [Fact]
    public void Flash_Default_IsWhileHeld()
    {
        var vm = new FlashEditorViewModel(new Project(), new FlashFunction());

        Assert.True(vm.IsHeld);
        Assert.False(vm.IsTimed);
        Assert.Equal(FlashEditorViewModel.DefaultTimedSeconds, vm.TimedSeconds);
        Assert.Contains("Press and hold", vm.PreviewCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Flash_ChooseTimed_UsesOneSecond_ThenHeldAndBack_RemembersTheTime()
    {
        var flash = new FlashFunction();
        var vm = new FlashEditorViewModel(new Project(), flash);

        vm.IsTimed = true;
        Assert.Equal(FlashEditorViewModel.DefaultTimedSeconds, vm.DurationSeconds);

        vm.TimedSeconds = 2.5;
        vm.IsHeld = true;
        Assert.Equal(0, vm.DurationSeconds);
        Assert.Equal(2.5, vm.TimedSeconds);

        vm.IsTimed = true;
        vm.Save();
        Assert.Equal(2.5, flash.DurationSeconds);
    }

    [Fact]
    public void Flash_TimedSecondsWhileHeld_DoesNotChangeTheFlash()
    {
        var vm = new FlashEditorViewModel(new Project(), new FlashFunction());

        vm.TimedSeconds = 3;

        Assert.True(vm.IsHeld);
        Assert.Equal(0, vm.DurationSeconds);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Flash_TimedSeconds_NeverBelowMinimum()
    {
        var vm = new FlashEditorViewModel(new Project(), new FlashFunction { DurationSeconds = 1 });

        vm.TimedSeconds = 0;

        Assert.Equal(FlashEditorViewModel.MinTimedSeconds, vm.DurationSeconds);
        Assert.True(vm.IsTimed);
    }

    [Fact]
    public void Flash_PreviewCaption_NamesThePresetColour()
    {
        var vm = new FlashEditorViewModel(new Project(), new FlashFunction { Color = new RgbColor(255, 0, 0) });

        Assert.Contains("full red", vm.PreviewCaption, StringComparison.Ordinal);
    }

    // ─────────────── Strobe (safety rule 8) ───────────────

    [Fact]
    public void Strobe_NotAllowedInSettings_PreviewIsSteadyAndSaysSo()
    {
        var settings = CreateSettings(allow: false, max: 10);
        var vm = new StrobeEditorViewModel(new Project(), new StrobeFunction(), settings);

        Assert.True(vm.IsStrobeDisabled);
        Assert.Equal(0, vm.EffectiveRateHz);
        Assert.All(vm.PreviewPoints, p => Assert.Equal(1, p.Level));
        Assert.Contains("turned off in Settings", vm.PreviewCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Strobe_FasterThanSettingsMaximum_ShowsCapNote()
    {
        var settings = CreateSettings(allow: true, max: 10);
        var vm = new StrobeEditorViewModel(new Project(), new StrobeFunction(), settings);

        vm.RateHz = 20;

        Assert.True(vm.ShowRateCap);
        Assert.Equal(10, vm.EffectiveRateHz);
        Assert.Contains("flashes at 10, not 20", vm.RateCapText, StringComparison.Ordinal);
    }

    [Fact]
    public void Strobe_SettingsChangeWhileOpen_UpdatesNotes()
    {
        var settings = CreateSettings(allow: false, max: 10);
        var vm = new StrobeEditorViewModel(new Project(), new StrobeFunction(), settings);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Update(s => s.AllowStrobe = true);

        Assert.False(vm.IsStrobeDisabled);
        Assert.Equal(StrobeFunction.DefaultRateHz, vm.EffectiveRateHz);
        Assert.Contains(nameof(StrobeEditorViewModel.IsStrobeDisabled), changed);
        Assert.Contains(nameof(StrobeEditorViewModel.PreviewPoints), changed);
        vm.Detach();
    }

    [Fact]
    public void Strobe_AfterDetach_IgnoresSettingsChanges()
    {
        var settings = CreateSettings(allow: false, max: 10);
        var vm = new StrobeEditorViewModel(new Project(), new StrobeFunction(), settings);
        vm.Detach();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Update(s => s.AllowStrobe = true);

        Assert.Empty(changed);
    }

    [Fact]
    public void Strobe_SpeedText_SaysFlashesPerSecond()
    {
        var vm = new StrobeEditorViewModel(new Project(), new StrobeFunction(), CreateSettings(allow: true, max: 10));

        vm.RateHz = 2;

        Assert.StartsWith("= ", vm.SpeedText);
        Assert.Contains("2", vm.SpeedText);
    }

    [Fact]
    public void Strobe_PresetsAndLengths_FollowTheValues()
    {
        var vm = new StrobeEditorViewModel(new Project(), new StrobeFunction(), CreateSettings(allow: true, max: 10));

        Assert.Equal("Medium", vm.SpeedPresets.Selected?.Label);
        Assert.Equal("Normal", vm.LengthPresets.Selected?.Label);

        vm.LengthPresets.Choices[0].IsSelected = true;
        vm.SpeedPresets.Choices[2].IsSelected = true;

        Assert.Equal(20, vm.DutyPercent);
        Assert.Equal(StrobeEditorViewModel.FastRateHz, vm.RateHz);
        Assert.False(vm.ShowRateCap);
    }

    [Fact]
    public void Strobe_ModeRadios_SwitchMode()
    {
        var strobe = new StrobeFunction();
        var vm = new StrobeEditorViewModel(new Project(), strobe, CreateSettings(allow: true, max: 10));

        vm.IsHardwareMode = true;
        vm.IsSoftwareMode = false;
        vm.UsesCustomHardwareValue = true;
        vm.HardwareValue = 40;
        vm.Save();

        Assert.Equal(StrobeMode.Hardware, strobe.Mode);
        Assert.Equal((byte)40, strobe.HardwareValue);
    }

    private AppSettingsService CreateSettings(bool allow, int max)
    {
        var settings = new AppSettingsService(NullLogger<AppSettingsService>.Instance, _settingsPath);
        settings.Update(s =>
        {
            s.AllowStrobe = allow;
            s.MaxStrobeRateHz = max;
        });
        return settings;
    }
}
