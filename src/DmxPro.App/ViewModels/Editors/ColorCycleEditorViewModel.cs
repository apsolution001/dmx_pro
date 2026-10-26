using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Colour cycle editor, kept simple for people who are not lighting experts: the lights (ordered), the colours in
/// play order (add from the scene presets or a custom colour, duplicate, remove, reorder, quick fills "Rainbow" /
/// "Warm" / "Cool" with undo), one hold and one fade time for every colour, "Spread across lights" as 0–100 %, and an
/// on-screen preview (one round as a strip, and the colour of each light at the start).
/// </summary>
/// <remarks>
/// The model stores the spread as <see cref="ColorCycleFunction.FixtureOffsetSeconds"/> (delay of each light behind the
/// previous one). The editor keeps the percentage as the user's intent: 100 % = one round spread evenly across all
/// lights. When the colours, times or lights change, the offset is recalculated so the wave keeps its shape.
/// </remarks>
public sealed partial class ColorCycleEditorViewModel : EffectEditorViewModel
{
    /// <summary>Most lights drawn in the preview; the rest are summarised as "+ N more".</summary>
    public const int MaxPreviewLights = 24;

    private double _holdSeconds;
    private double _fadeSeconds;
    private double _fixtureOffsetSeconds;

    // The user's spread in percent; null = not known yet (fewer than two lights or no colours when loaded), then the
    // saved offset is kept untouched until it can be turned into a percentage.
    private double? _spreadPercent;
    private int _lightCount;
    private List<RgbColor>? _colorsBeforeFill;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedColor), nameof(HasNoSelectedColor), nameof(SelectedColorTitle))]
    [NotifyCanExecuteChangedFor(nameof(RemoveColorCommand), nameof(DuplicateColorCommand), nameof(MoveColorUpCommand), nameof(MoveColorDownCommand))]
    private ColorCycleEntryViewModel? _selectedColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoFill))]
    private string? _fillMessage;

    public ColorCycleEditorViewModel(Project project, ColorCycleFunction cycle)
        : base(project, cycle)
    {
        ColorEditor = new ColorEditorViewModel(OnColorEdited);
        Fixtures.PropertyChanged += OnFixturesPropertyChanged;
        LoadFromOriginal();
    }

    public ObservableCollection<ColorCycleEntryViewModel> Colors { get; } = [];

    /// <summary>Edits <see cref="SelectedColor"/> (presets, R/G/B, #RRGGBB).</summary>
    public ColorEditorViewModel ColorEditor { get; }

    /// <summary>The scene editor's colour presets: one click adds that colour.</summary>
    public IReadOnlyList<SceneEditorColorPreset> Presets => SceneEditorColorPreset.All;

    /// <summary>Quick fills that replace the colour list.</summary>
    public IReadOnlyList<ColorCyclePalette> Palettes => ColorCyclePalette.All;

    /// <summary>One round as a strip, one slot per colour.</summary>
    public ObservableCollection<ColorCyclePreviewSlot> PreviewSlots { get; } = [];

    /// <summary>The colour each light shows when the cycle starts (at most <see cref="MaxPreviewLights"/>).</summary>
    public ObservableCollection<ColorCyclePreviewLight> PreviewLights { get; } = [];

    public bool HasSelectedColor => SelectedColor is not null;

    public bool HasNoSelectedColor => SelectedColor is null;

    public bool HasNoColors => Colors.Count == 0;

    public bool HasColors => Colors.Count > 0;

    /// <summary>"3 colours".</summary>
    public string ColorCountText => Colors.Count == 1 ? "1 colour" : $"{Colors.Count} colours";

    /// <summary>"Change colour 2 · Green".</summary>
    public string SelectedColorTitle => SelectedColor is { } c ? $"Change colour {c.Number} · {c.Name}" : "Change colour";

    public bool CanUndoFill => _colorsBeforeFill is not null && FillMessage is not null;

    public double? HoldSeconds
    {
        get => _holdSeconds;
        set
        {
            SetNumber(ref _holdSeconds, value, 0, FunctionEditing.MaxSeconds);
            OnTimingChanged();
        }
    }

    public double? FadeSeconds
    {
        get => _fadeSeconds;
        set
        {
            SetNumber(ref _fadeSeconds, value, 0, FunctionEditing.MaxSeconds);
            OnTimingChanged();
        }
    }

    /// <summary>Delay of each light behind the previous one, seconds (what is saved); set through <see cref="SpreadPercent"/>.</summary>
    public double FixtureOffsetSeconds => _fixtureOffsetSeconds;

    /// <summary>"Spread across lights", 0–100 %: 0 = every light the same colour, 100 = one round spread over all lights.</summary>
    public double SpreadPercent
    {
        get => Math.Min(_spreadPercent ?? 0, ColorCycleTiming.MaxSpreadPercent);
        set
        {
            if (!CanSpread || !double.IsFinite(value))
            {
                OnPropertyChanged();
                return;
            }

            var next = Math.Round(Math.Clamp(value, 0, ColorCycleTiming.MaxSpreadPercent), 1);
            if (next == SpreadPercent && _spreadPercent is not null)
            {
                return;
            }

            _spreadPercent = next;
            ApplySpread(markDirty: true);
            RefreshDerived();
        }
    }

    /// <summary>Spreading needs at least one colour and two lights.</summary>
    public bool CanSpread => ColorCycleTiming.CanSpread(Colors.Count, _lightCount);

    /// <summary>"40 %".</summary>
    public string SpreadText => $"{Math.Round(_spreadPercent ?? 0):0} %";

    /// <summary>Plain explanation of the current spread.</summary>
    public string SpreadDetail
    {
        get
        {
            if (Colors.Count == 0)
            {
                return "Add colours first.";
            }

            if (_lightCount < 2)
            {
                return "Choose 2 or more lights to spread the colours across them.";
            }

            return _fixtureOffsetSeconds <= 0
                ? "All lights show the same colour at the same time."
                : $"Each light runs {EditorTiming.FormatDuration(_fixtureOffsetSeconds)} behind the one before it.";
        }
    }

    /// <summary>"One round: 6 s (3 colours × 2 s)".</summary>
    public string RoundText
    {
        get
        {
            if (Colors.Count == 0)
            {
                return "Add at least one colour.";
            }

            var slot = ColorCycleTiming.SlotSeconds(_holdSeconds, _fadeSeconds);
            var round = ColorCycleTiming.RoundSeconds(Colors.Count, _holdSeconds, _fadeSeconds);
            return $"One round: {EditorTiming.FormatDuration(round)} ({ColorCountText} × {EditorTiming.FormatDuration(slot)}), then it starts again.";
        }
    }

    public bool HasPreviewLights => PreviewLights.Count > 0;

    /// <summary>"+ 6 more lights" when the preview is cut off; null otherwise.</summary>
    public string? MorePreviewLightsText => _lightCount > MaxPreviewLights ? $"+ {_lightCount - MaxPreviewLights} more lights" : null;

    /// <summary>Why the light preview is empty; null when it shows lights.</summary>
    public string? PreviewLightsHint => Colors.Count == 0
        ? "Add colours to see the preview."
        : _lightCount == 0 ? "Choose lights to see how the colours spread across them." : null;

    private ColorCycleFunction Cycle => (ColorCycleFunction)Target;

    public override void Detach()
    {
        Fixtures.PropertyChanged -= OnFixturesPropertyChanged;
        base.Detach();
    }

    protected override void LoadEffect()
    {
        _holdSeconds = FunctionEditing.Clamp(Cycle.HoldSeconds, 0, FunctionEditing.MaxSeconds);
        _fadeSeconds = FunctionEditing.Clamp(Cycle.FadeSeconds, 0, FunctionEditing.MaxSeconds);
        _fixtureOffsetSeconds = double.IsFinite(Cycle.FixtureOffsetSeconds) ? Math.Max(Cycle.FixtureOffsetSeconds, 0) : 0;
        _spreadPercent = null;
        _lightCount = Fixtures.FixtureIds.Count;
        _colorsBeforeFill = null;
        FillMessage = null;
        OnPropertyChanged(nameof(HoldSeconds));
        OnPropertyChanged(nameof(FadeSeconds));
        RebuildColors(Cycle.Colors, Cycle.Colors.Count > 0 ? 0 : -1);
    }

    protected override void ApplyEffect()
    {
        Cycle.Colors = Colors.Select(c => c.Color).ToList();
        Cycle.HoldSeconds = _holdSeconds;
        Cycle.FadeSeconds = _fadeSeconds;
        Cycle.FixtureOffsetSeconds = _fixtureOffsetSeconds;
    }

    partial void OnSelectedColorChanged(ColorCycleEntryViewModel? value)
    {
        if (value is not null)
        {
            ColorEditor.SetColorSilently(value.Color);
        }
    }

    /// <summary>Adds a custom colour after the selected one (a copy of it, or white) and selects it for changing.</summary>
    [RelayCommand]
    private void AddColor() => InsertAfterSelection(SelectedColor?.Color ?? new RgbColor(255, 255, 255));

    /// <summary>Adds a preset colour after the selected one.</summary>
    [RelayCommand]
    private void AddPreset(SceneEditorColorPreset? preset)
    {
        if (preset is not null && RgbColor.TryParseHex(preset.Swatch, out var color))
        {
            InsertAfterSelection(color);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedColor))]
    private void DuplicateColor()
    {
        if (SelectedColor is { } selected)
        {
            InsertAfterSelection(selected.Color);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedColor))]
    private void RemoveColor()
    {
        if (SelectedColor is not { } selected)
        {
            return;
        }

        var index = Colors.IndexOf(selected);
        var colors = CurrentColors();
        colors.RemoveAt(index);
        CommitColors(colors, colors.Count == 0 ? -1 : Math.Min(index, colors.Count - 1));
    }

    [RelayCommand(CanExecute = nameof(CanMoveColorUp))]
    private void MoveColorUp() => MoveColor(-1);

    [RelayCommand(CanExecute = nameof(CanMoveColorDown))]
    private void MoveColorDown() => MoveColor(1);

    /// <summary>Replaces the colours with a quick fill; <see cref="UndoFillCommand"/> brings the old ones back.</summary>
    [RelayCommand]
    private void ApplyPalette(ColorCyclePalette? palette)
    {
        if (palette is null)
        {
            return;
        }

        var before = CurrentColors();
        if (before.SequenceEqual(palette.Colors))
        {
            return;
        }

        CommitColors([.. palette.Colors], 0);
        if (before.Count > 0)
        {
            _colorsBeforeFill = before;
            FillMessage = before.Count == 1
                ? $"Replaced 1 colour with {palette.Name}."
                : $"Replaced {before.Count} colours with {palette.Name}.";
        }
    }

    [RelayCommand]
    private void UndoFill()
    {
        if (_colorsBeforeFill is not { } before)
        {
            return;
        }

        CommitColors(before, before.Count > 0 ? 0 : -1);
    }

    private bool CanMoveColorUp => SelectedColor is { } c && Colors.IndexOf(c) > 0;

    private bool CanMoveColorDown => SelectedColor is { } c && Colors.IndexOf(c) is var i && i >= 0 && i < Colors.Count - 1;

    private List<RgbColor> CurrentColors() => Colors.Select(c => c.Color).ToList();

    private void InsertAfterSelection(RgbColor color)
    {
        var colors = CurrentColors();
        var index = SelectedColor is { } selected ? Colors.IndexOf(selected) + 1 : colors.Count;
        colors.Insert(index, color);
        CommitColors(colors, index);
    }

    private void MoveColor(int offset)
    {
        if (SelectedColor is not { } selected)
        {
            return;
        }

        var colors = CurrentColors();
        var index = FunctionEditing.MoveItem(colors, Colors.IndexOf(selected), offset);
        CommitColors(colors, index);
    }

    /// <summary>A user edit of the colour list: rebuild, keep the spread, forget the quick-fill undo, mark changed.</summary>
    private void CommitColors(IReadOnlyList<RgbColor> colors, int select)
    {
        _colorsBeforeFill = null;
        FillMessage = null;
        RebuildColors(colors, select);
        MarkDirty();
    }

    private void RebuildColors(IReadOnlyList<RgbColor> colors, int select)
    {
        Colors.Clear();
        for (var i = 0; i < colors.Count; i++)
        {
            Colors.Add(new ColorCycleEntryViewModel(colors[i], i + 1));
        }

        SelectedColor = select >= 0 && select < Colors.Count ? Colors[select] : null;
        MoveColorUpCommand.NotifyCanExecuteChanged();
        MoveColorDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoColors));
        OnPropertyChanged(nameof(HasColors));
        OnPropertyChanged(nameof(ColorCountText));
        ApplySpread(markDirty: false);
        RefreshDerived();
    }

    private void OnTimingChanged()
    {
        if (IsLoading)
        {
            return;
        }

        ApplySpread(markDirty: true);
        RefreshDerived();
    }

    private void OnFixturesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // CountText changes on every rebuild of the list (choose, remove, move, patch refresh, load), before the base
        // editor marks the draft changed, so the preview it sends already has the recalculated offset.
        if (e.PropertyName != nameof(FixtureListEditorViewModel.CountText))
        {
            return;
        }

        _lightCount = Fixtures.FixtureIds.Count;
        if (IsLoading)
        {
            return;
        }

        ApplySpread(markDirty: true);
        RefreshDerived();
    }

    /// <summary>
    /// Keeps the saved offset in line with the spread percentage. While the percentage is unknown (loaded without
    /// lights or colours) it is derived from the saved offset as soon as it can be, so nothing changes on its own.
    /// </summary>
    private void ApplySpread(bool markDirty)
    {
        if (!CanSpread)
        {
            return;
        }

        var round = ColorCycleTiming.RoundSeconds(Colors.Count, _holdSeconds, _fadeSeconds);
        if (_spreadPercent is null)
        {
            _spreadPercent = ColorCycleTiming.SpreadForOffset(_fixtureOffsetSeconds, round, _lightCount);
            return;
        }

        var offset = ColorCycleTiming.OffsetForSpread(_spreadPercent.Value, round, _lightCount);
        if (offset == _fixtureOffsetSeconds)
        {
            return;
        }

        _fixtureOffsetSeconds = offset;
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(RoundText));
        OnPropertyChanged(nameof(CanSpread));
        OnPropertyChanged(nameof(SpreadPercent));
        OnPropertyChanged(nameof(SpreadText));
        OnPropertyChanged(nameof(SpreadDetail));
        OnPropertyChanged(nameof(FixtureOffsetSeconds));
        OnPropertyChanged(nameof(SelectedColorTitle));
        RebuildPreview();
    }

    private void RebuildPreview()
    {
        var colors = CurrentColors();
        var slot = ColorCycleTiming.SlotSeconds(_holdSeconds, _fadeSeconds);
        var holdShare = colors.Count < 2 ? 1 : Math.Clamp((slot - _fadeSeconds) / slot, 0, 1);

        PreviewSlots.Clear();
        for (var i = 0; i < colors.Count; i++)
        {
            var next = colors[(i + 1) % colors.Count];
            var name = Colors[i].Name;
            var toolTip = colors.Count < 2
                ? $"{i + 1}. {name}"
                : $"{i + 1}. {name}, then blends into {ColorCycleColorNames.NameOf(next)}";
            PreviewSlots.Add(new ColorCyclePreviewSlot(colors[i].ToHex(), next.ToHex(), holdShare, toolTip));
        }

        PreviewLights.Clear();
        if (colors.Count > 0)
        {
            var shown = Math.Min(_lightCount, MaxPreviewLights);
            var lightColors = ColorCycleTiming.LightColorsAtStart(colors, _holdSeconds, _fadeSeconds, _fixtureOffsetSeconds, shown);
            for (var i = 0; i < shown; i++)
            {
                var lightName = i < Fixtures.Items.Count ? Fixtures.Items[i].Name : $"Light {i + 1}";
                var color = lightColors[i];
                PreviewLights.Add(new ColorCyclePreviewLight(i + 1, color.ToHex(), ColorCycleColorNames.NameOf(color), lightName));
            }
        }

        OnPropertyChanged(nameof(HasPreviewLights));
        OnPropertyChanged(nameof(MorePreviewLightsText));
        OnPropertyChanged(nameof(PreviewLightsHint));
    }

    private void OnColorEdited(RgbColor color)
    {
        if (SelectedColor is { } selected)
        {
            selected.Color = color;
            _colorsBeforeFill = null;
            FillMessage = null;
            MarkDirty();
            RefreshDerived();
        }
    }
}
