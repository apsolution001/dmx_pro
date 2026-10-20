using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Color cycle editor: fixtures, an ordered colour list (add / remove / move; the selected colour is edited with
/// presets and R/G/B), hold and fade time, per-fixture offset and "use the white channel".
/// </summary>
public sealed partial class ColorCycleEditorViewModel : EffectEditorViewModel
{
    private double _holdSeconds;
    private double _fadeSeconds;
    private double _fixtureOffsetSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedColor), nameof(HasNoSelectedColor))]
    [NotifyCanExecuteChangedFor(nameof(RemoveColorCommand), nameof(MoveColorUpCommand), nameof(MoveColorDownCommand))]
    private ColorCycleEntryViewModel? _selectedColor;

    public ColorCycleEditorViewModel(Project project, ColorCycleFunction cycle)
        : base(project, cycle)
    {
        ColorEditor = new ColorEditorViewModel(OnColorEdited);
        LoadFromOriginal();
    }

    public ObservableCollection<ColorCycleEntryViewModel> Colors { get; } = [];

    /// <summary>Edits <see cref="SelectedColor"/>.</summary>
    public ColorEditorViewModel ColorEditor { get; }

    public bool HasSelectedColor => SelectedColor is not null;

    public bool HasNoSelectedColor => SelectedColor is null;

    public bool HasNoColors => Colors.Count == 0;

    public double? HoldSeconds
    {
        get => _holdSeconds;
        set
        {
            SetNumber(ref _holdSeconds, value, 0, FunctionEditing.MaxSeconds);
            OnPropertyChanged(nameof(CycleText));
        }
    }

    public double? FadeSeconds
    {
        get => _fadeSeconds;
        set
        {
            SetNumber(ref _fadeSeconds, value, 0, FunctionEditing.MaxSeconds);
            OnPropertyChanged(nameof(CycleText));
        }
    }

    public double? FixtureOffsetSeconds
    {
        get => _fixtureOffsetSeconds;
        set => SetNumber(ref _fixtureOffsetSeconds, value, 0, FunctionEditing.MaxSeconds);
    }

    /// <summary>"One round: 6 s (3 colours × (hold 1 s + fade 1 s))".</summary>
    public string CycleText => Colors.Count == 0
        ? "Add at least one colour."
        : $"One round: {EditorTiming.FormatDuration(Colors.Count * (_holdSeconds + _fadeSeconds))}";

    private ColorCycleFunction Cycle => (ColorCycleFunction)Original;

    protected override void LoadEffect()
    {
        HoldSeconds = Cycle.HoldSeconds;
        FadeSeconds = Cycle.FadeSeconds;
        FixtureOffsetSeconds = Cycle.FixtureOffsetSeconds;
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

        MoveColorUpCommand.NotifyCanExecuteChanged();
        MoveColorDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Adds a colour after the selected one: a copy of it (or white) to adjust.</summary>
    [RelayCommand]
    private void AddColor()
    {
        var colors = Colors.Select(c => c.Color).ToList();
        var index = SelectedColor is { } selected ? Colors.IndexOf(selected) + 1 : colors.Count;
        colors.Insert(index, SelectedColor?.Color ?? new RgbColor(255, 255, 255));
        RebuildColors(colors, index);
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedColor))]
    private void RemoveColor()
    {
        if (SelectedColor is not { } selected)
        {
            return;
        }

        var index = Colors.IndexOf(selected);
        var colors = Colors.Select(c => c.Color).ToList();
        colors.RemoveAt(index);
        RebuildColors(colors, colors.Count == 0 ? -1 : Math.Min(index, colors.Count - 1));
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanMoveColorUp))]
    private void MoveColorUp() => MoveColor(-1);

    [RelayCommand(CanExecute = nameof(CanMoveColorDown))]
    private void MoveColorDown() => MoveColor(1);

    private bool CanMoveColorUp => SelectedColor is { } c && Colors.IndexOf(c) > 0;

    private bool CanMoveColorDown => SelectedColor is { } c && Colors.IndexOf(c) is var i && i >= 0 && i < Colors.Count - 1;

    private void MoveColor(int offset)
    {
        if (SelectedColor is not { } selected)
        {
            return;
        }

        var colors = Colors.Select(c => c.Color).ToList();
        var index = FunctionEditing.MoveItem(colors, Colors.IndexOf(selected), offset);
        RebuildColors(colors, index);
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
        OnPropertyChanged(nameof(HasNoColors));
        OnPropertyChanged(nameof(CycleText));
    }

    private void OnColorEdited(RgbColor color)
    {
        if (SelectedColor is { } selected)
        {
            selected.Color = color;
            MarkDirty();
        }
    }
}
