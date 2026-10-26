using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Chaser editor: the step list (colour swatch, function, hold, fade in, fade out; several steps can be selected) with
/// add / remove / duplicate / move / drag-to-move, a colour strip of one cycle in play order, the playback settings
/// (run order, direction) and "Step timing": the times new steps get, which can also be applied to the selected steps
/// or to all steps at once. "Add" offers every listed function except the chaser itself and functions that would make
/// it play itself (<see cref="FunctionEditing.CanPlayInChaser"/>). Steps reference functions by id; edits stay in the
/// draft until Save. List logic lives in <see cref="ChaserStepListEditing"/>.
/// </summary>
public sealed partial class ChaserEditorViewModel : FunctionEditorViewModel
{
    private double _newHoldSeconds = ChaserStep.DefaultHoldSeconds;
    private double _newFadeInSeconds;
    private double _newFadeOutSeconds;

    // Set while the editor changes many rows at once: row selection callbacks then wait for one refresh at the end.
    private bool _bulkUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoop), nameof(IsPlayOnce), nameof(IsBackAndForth), nameof(EndOfCycleText))]
    private RunOrder _runOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForward), nameof(IsBackward))]
    private RunDirection _direction;

    [ObservableProperty]
    private string _pickerSearch = string.Empty;

    [ObservableProperty]
    private bool _isPickerOpen;

    public ChaserEditorViewModel(Project project, ChaserFunction chaser)
        : base(project, chaser)
    {
        LoadFromOriginal();
    }

    public ObservableCollection<ChaserStepRowViewModel> Steps { get; } = [];

    /// <summary>The steps of one cycle in the order they play (a step can appear twice when going back and forth).</summary>
    public ObservableCollection<ChaserStepRowViewModel> PlayOrderSteps { get; } = [];

    /// <summary>Functions the "Add steps" picker offers (filtered by <see cref="PickerSearch"/>).</summary>
    public ObservableCollection<ChaserPickerItem> PickerItems { get; } = [];

    public bool HasNoSteps => Steps.Count == 0;

    public bool HasSteps => Steps.Count > 0;

    public bool HasNoPickerItems => PickerItems.Count == 0;

    public int SelectedCount => Steps.Count(s => s.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    /// <summary>"4 steps" or "2 of 4 selected".</summary>
    public string StepCountText => SelectedCount is var selected and > 0
        ? $"{selected} of {Steps.Count} selected"
        : Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";

    /// <summary>"Cycle: 6.5 s" / "Runs once: 7 s" / "There and back: 10 s" — the way the engine plays it.</summary>
    public string CycleText => EditorTiming.CycleText(
        Steps.Count,
        FunctionEditing.CycleSeconds(Steps.Select(s => (s.Step.FadeInSeconds, s.Step.HoldSeconds, s.Step.FadeOutSeconds)).ToList(), RunOrder, Direction),
        RunOrder);

    /// <summary>What happens after the colour strip's last block (text, so the run order is not shown by colour alone).</summary>
    public string EndOfCycleText => RunOrder == RunOrder.SingleShot ? "then stops" : "then starts again";

    /// <summary>"Apply to selected (2)".</summary>
    public string ApplyToSelectedText => SelectedCount is var selected and > 0 ? $"Apply to selected ({selected})" : "Apply to selected";

    public bool IsLoop
    {
        get => RunOrder == RunOrder.Loop;
        set => SetRunOrder(value, RunOrder.Loop);
    }

    public bool IsPlayOnce
    {
        get => RunOrder == RunOrder.SingleShot;
        set => SetRunOrder(value, RunOrder.SingleShot);
    }

    public bool IsBackAndForth
    {
        get => RunOrder == RunOrder.PingPong;
        set => SetRunOrder(value, RunOrder.PingPong);
    }

    public bool IsForward
    {
        get => Direction == RunDirection.Forward;
        set
        {
            if (value)
            {
                Direction = RunDirection.Forward;
            }
        }
    }

    public bool IsBackward
    {
        get => Direction == RunDirection.Backward;
        set
        {
            if (value)
            {
                Direction = RunDirection.Backward;
            }
        }
    }

    /// <summary>Hold of new steps and of "Apply to …", seconds (0–3600). Not part of the chaser until applied.</summary>
    public double? NewHoldSeconds
    {
        get => _newHoldSeconds;
        set => SetTimingField(ref _newHoldSeconds, value);
    }

    /// <summary>Fade in of new steps and of "Apply to …", seconds.</summary>
    public double? NewFadeInSeconds
    {
        get => _newFadeInSeconds;
        set => SetTimingField(ref _newFadeInSeconds, value);
    }

    /// <summary>Fade out of new steps and of "Apply to …", seconds.</summary>
    public double? NewFadeOutSeconds
    {
        get => _newFadeOutSeconds;
        set => SetTimingField(ref _newFadeOutSeconds, value);
    }

    private ChaserFunction Chaser => (ChaserFunction)Target;

    private List<int> SelectedIndexes => Steps.Select((row, i) => (row, i)).Where(x => x.row.IsSelected).Select(x => x.i).ToList();

    protected override void LoadDraft()
    {
        RunOrder = Chaser.RunOrder;
        Direction = Chaser.Direction;
        PickerSearch = string.Empty;
        IsPickerOpen = false;

        // "Step timing" starts with the first step's times, so new steps match the existing ones.
        var first = Chaser.Steps.FirstOrDefault();
        _newHoldSeconds = first?.HoldSeconds ?? ChaserStep.DefaultHoldSeconds;
        _newFadeInSeconds = first?.FadeInSeconds ?? 0;
        _newFadeOutSeconds = first?.FadeOutSeconds ?? 0;
        OnPropertyChanged(nameof(NewHoldSeconds));
        OnPropertyChanged(nameof(NewFadeInSeconds));
        OnPropertyChanged(nameof(NewFadeOutSeconds));

        RunBulk(() =>
        {
            Steps.Clear();
            foreach (var step in Chaser.Steps)
            {
                Steps.Add(CreateRow(step.Clone()));
            }
        });
        OnStepsChanged(markDirty: false);
        RefreshPicker();
    }

    protected override void ApplyDraft()
    {
        Chaser.Steps = Steps.Select(s => s.Step.Clone()).ToList();
        Chaser.RunOrder = RunOrder;
        Chaser.Direction = Direction;
    }

    partial void OnRunOrderChanged(RunOrder value) => OnPlaybackChanged();

    partial void OnDirectionChanged(RunDirection value) => OnPlaybackChanged();

    partial void OnPickerSearchChanged(string value) => RefreshPicker();

    partial void OnIsPickerOpenChanged(bool value)
    {
        if (value)
        {
            // Every opening starts with a fresh, complete list (functions may have been added meanwhile).
            PickerSearch = string.Empty;
            RefreshPicker();
        }
    }

    [RelayCommand]
    private void OpenPicker() => IsPickerOpen = true;

    /// <summary>
    /// Adds the picked functions (a single <see cref="ChaserPickerItem"/> or a list of them) as steps after the last
    /// selected step (or at the end), with the "Step timing" times. The new steps become the selection; the picker closes.
    /// </summary>
    [RelayCommand]
    private void AddSteps(object? parameter)
    {
        List<ChaserPickerItem> picked = parameter switch
        {
            ChaserPickerItem item => new List<ChaserPickerItem> { item },
            IList list => list.OfType<ChaserPickerItem>().ToList(),
            _ => new List<ChaserPickerItem>(),
        };

        var added = new List<ChaserStepRowViewModel>();
        RunBulk(() =>
        {
            var index = ChaserStepListEditing.InsertIndex(Steps.Count, SelectedIndexes);
            foreach (var item in picked)
            {
                if (!Project.CanPlayInChaser(Original.Id, item.Function))
                {
                    continue;
                }

                var row = CreateRow(new ChaserStep
                {
                    FunctionId = item.Function.Id,
                    HoldSeconds = _newHoldSeconds,
                    FadeInSeconds = _newFadeInSeconds,
                    FadeOutSeconds = _newFadeOutSeconds,
                });
                Steps.Insert(index++, row);
                added.Add(row);
            }

            if (added.Count > 0)
            {
                SelectOnly(added);
            }
        });

        if (added.Count == 0)
        {
            return;
        }

        IsPickerOpen = false;
        OnStepsChanged(markDirty: true);
    }

    /// <summary>Enter in the picker's search box: adds the first function that matches the search.</summary>
    [RelayCommand]
    private void AddFirstMatch()
    {
        if (PickerItems.FirstOrDefault() is { } first)
        {
            AddSteps(first);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveSteps()
    {
        var selected = SelectedIndexes;
        if (selected.Count == 0)
        {
            return;
        }

        RunBulk(() =>
        {
            var next = ChaserStepListEditing.SelectionAfterRemove(Steps.Count, selected);
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                Steps.RemoveAt(selected[i]);
            }

            if (next >= 0)
            {
                Steps[next].IsSelected = true;
            }
        });
        OnStepsChanged(markDirty: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateSteps()
    {
        var selected = SelectedIndexes;
        if (selected.Count == 0)
        {
            return;
        }

        RunBulk(() =>
        {
            var index = ChaserStepListEditing.InsertIndex(Steps.Count, selected);
            var copies = selected.Select(i => CreateRow(Steps[i].Step.Clone())).ToList();
            foreach (var copy in copies)
            {
                Steps.Insert(index++, copy);
            }

            SelectOnly(copies);
        });
        OnStepsChanged(markDirty: true);
    }

    [RelayCommand(CanExecute = nameof(CanMoveStepsUp))]
    private void MoveStepsUp() => ApplyOrder(ChaserStepListEditing.MoveBy(Steps.Count, SelectedIndexes, -1));

    [RelayCommand(CanExecute = nameof(CanMoveStepsDown))]
    private void MoveStepsDown() => ApplyOrder(ChaserStepListEditing.MoveBy(Steps.Count, SelectedIndexes, 1));

    /// <summary>Drag and drop: moves the selected steps into the gap before list position <paramref name="insertBefore"/>.</summary>
    [RelayCommand]
    private void MoveStepsTo(int insertBefore) => ApplyOrder(ChaserStepListEditing.MoveTo(Steps.Count, SelectedIndexes, insertBefore));

    /// <summary>Selects every step (Ctrl+A in the list does the same through the list itself).</summary>
    [RelayCommand]
    private void SelectAllSteps()
    {
        RunBulk(() => SelectOnly(Steps));
        OnSelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ApplyTimingToSelected() => ApplyTiming(Steps.Where(s => s.IsSelected).ToList());

    [RelayCommand(CanExecute = nameof(HasSteps))]
    private void ApplyTimingToAll() => ApplyTiming(Steps.ToList());

    private bool CanMoveStepsUp => ChaserStepListEditing.MoveBy(Steps.Count, SelectedIndexes, -1) is not null;

    private bool CanMoveStepsDown => ChaserStepListEditing.MoveBy(Steps.Count, SelectedIndexes, 1) is not null;

    private void ApplyTiming(IReadOnlyList<ChaserStepRowViewModel> rows)
    {
        if (ChaserStepListEditing.ApplyTiming(rows.Select(r => r.Step), _newHoldSeconds, _newFadeInSeconds, _newFadeOutSeconds) == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            row.RefreshTiming();
        }

        OnTimingChanged();
    }

    private void ApplyOrder(int[]? order)
    {
        if (order is null)
        {
            return;
        }

        RunBulk(() => ChaserStepListEditing.ApplyOrder(Steps, order));
        OnStepsChanged(markDirty: true);
    }

    private ChaserStepRowViewModel CreateRow(ChaserStep step)
    {
        var function = Project.FindFunction(step.FunctionId);
        var color = ShowsLibraryItem.PreviewColor(function, Project)?.ToHex();
        return new ChaserStepRowViewModel(step, function, Steps.Count + 1, color, OnRowTimingChanged, OnRowSelectionChanged);
    }

    private void SelectOnly(IEnumerable<ChaserStepRowViewModel> rows)
    {
        var keep = rows.ToHashSet();
        foreach (var row in Steps)
        {
            row.IsSelected = keep.Contains(row);
        }
    }

    private void RunBulk(Action action)
    {
        var wasBulk = _bulkUpdate;
        _bulkUpdate = true;
        try
        {
            action();
        }
        finally
        {
            _bulkUpdate = wasBulk;
        }
    }

    /// <summary>After add / remove / move / load: renumber, rebuild the strip and refresh every readout.</summary>
    private void OnStepsChanged(bool markDirty)
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Number = i + 1;
        }

        RebuildPlayOrder();
        OnPropertyChanged(nameof(HasNoSteps));
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(CycleText));
        ApplyTimingToAllCommand.NotifyCanExecuteChanged();
        OnSelectionChanged();
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(StepCountText));
        OnPropertyChanged(nameof(ApplyToSelectedText));
        RemoveStepsCommand.NotifyCanExecuteChanged();
        DuplicateStepsCommand.NotifyCanExecuteChanged();
        MoveStepsUpCommand.NotifyCanExecuteChanged();
        MoveStepsDownCommand.NotifyCanExecuteChanged();
        ApplyTimingToSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnRowSelectionChanged(ChaserStepRowViewModel row)
    {
        if (!_bulkUpdate)
        {
            OnSelectionChanged();
        }
    }

    private void OnRowTimingChanged(StepTimingRowViewModel row) => OnTimingChanged();

    private void OnTimingChanged()
    {
        OnPropertyChanged(nameof(CycleText));
        MarkDirty();
    }

    private void OnPlaybackChanged()
    {
        RebuildPlayOrder();
        OnTimingChanged();
    }

    private void RebuildPlayOrder()
    {
        PlayOrderSteps.Clear();
        foreach (var index in ChaserStepListEditing.PlayOrder(Steps.Count, RunOrder, Direction))
        {
            PlayOrderSteps.Add(Steps[index]);
        }
    }

    private void SetRunOrder(bool isChecked, RunOrder order)
    {
        if (isChecked)
        {
            RunOrder = order;
        }
    }

    private void SetTimingField(ref double field, double? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (value is { } v && double.IsFinite(v))
        {
            field = FunctionEditing.Clamp(v, 0, FunctionEditing.MaxSeconds);
        }

        // Always re-raise so the box shows what was kept (clamped, or the old value for empty input).
        OnPropertyChanged(propertyName);
    }

    private void RefreshPicker()
    {
        var text = PickerSearch?.Trim() ?? string.Empty;
        PickerItems.Clear();
        foreach (var function in Project.FunctionListItems.OrderBy(f => f.Type).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!Project.CanPlayInChaser(Original.Id, function))
            {
                continue;
            }

            if (text.Length == 0
                || function.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || FunctionRowViewModel.TypeDisplayName(function.Type).Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                PickerItems.Add(new ChaserPickerItem(function, ShowsLibraryItem.PreviewColor(function, Project)?.ToHex()));
            }
        }

        OnPropertyChanged(nameof(HasNoPickerItems));
    }
}
