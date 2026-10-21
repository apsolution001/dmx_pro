using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Chaser editor (QLC+ chaser editor, compact): a step grid (function, fade in, hold, fade out) with add / remove /
/// duplicate / move, run order and direction, and the cycle time. "Add" offers every listed function except the
/// chaser itself and functions that would make it play itself (<see cref="FunctionEditing.CanPlayInChaser"/>).
/// Steps reference functions by id; edits stay in the draft until Save.
/// </summary>
public sealed partial class ChaserEditorViewModel : FunctionEditorViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveStepCommand), nameof(DuplicateStepCommand), nameof(MoveStepUpCommand), nameof(MoveStepDownCommand))]
    private ChaserStepRowViewModel? _selectedStep;

    [ObservableProperty]
    private RunOrder _runOrder;

    [ObservableProperty]
    private RunDirection _direction;

    [ObservableProperty]
    private string _pickerSearch = string.Empty;

    public ChaserEditorViewModel(Project project, ChaserFunction chaser)
        : base(project, chaser)
    {
        LoadFromOriginal();
    }

    public ObservableCollection<ChaserStepRowViewModel> Steps { get; } = [];

    /// <summary>Functions the "Add" flyout offers (filtered by <see cref="PickerSearch"/>).</summary>
    public ObservableCollection<FunctionPickerItem> PickerItems { get; } = [];

    public IReadOnlyList<EditorOption<RunOrder>> RunOrders => EditorTiming.RunOrders;

    public IReadOnlyList<EditorOption<RunDirection>> Directions => EditorTiming.Directions;

    public bool HasNoSteps => Steps.Count == 0;

    public bool HasNoPickerItems => PickerItems.Count == 0;

    /// <summary>"Cycle: 6.5 s" — how long one pass takes, the way the engine plays it.</summary>
    public string CycleText => EditorTiming.CycleText(
        Steps.Count,
        FunctionEditing.CycleSeconds(Steps.Select(s => (s.Step.FadeInSeconds, s.Step.HoldSeconds, s.Step.FadeOutSeconds)).ToList(), RunOrder, Direction),
        RunOrder);

    /// <summary>"4 steps".</summary>
    public string StepCountText => Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";

    private ChaserFunction Chaser => (ChaserFunction)Target;

    protected override void LoadDraft()
    {
        RunOrder = Chaser.RunOrder;
        Direction = Chaser.Direction;
        PickerSearch = string.Empty;
        RebuildSteps(Chaser.Steps.Select(s => s.Clone()).ToList(), select: null);
        RefreshPicker();
    }

    protected override void ApplyDraft()
    {
        Chaser.Steps = Steps.Select(s => s.Step.Clone()).ToList();
        Chaser.RunOrder = RunOrder;
        Chaser.Direction = Direction;
    }

    partial void OnRunOrderChanged(RunOrder value) => OnTimingChanged();

    partial void OnDirectionChanged(RunDirection value) => OnTimingChanged();

    partial void OnPickerSearchChanged(string value) => RefreshPicker();

    /// <summary>
    /// Adds the picked functions (a single <see cref="FunctionPickerItem"/> or the picker's selected items) as steps
    /// after the selected step, with the selected (or last) step's timing.
    /// </summary>
    [RelayCommand]
    private void AddSteps(object? parameter)
    {
        List<FunctionPickerItem> picked = parameter switch
        {
            FunctionPickerItem item => new List<FunctionPickerItem> { item },
            IList list => list.OfType<FunctionPickerItem>().ToList(),
            _ => new List<FunctionPickerItem>(),
        };
        if (picked.Count == 0)
        {
            return;
        }

        var template = SelectedStep?.Step ?? Steps.LastOrDefault()?.Step;
        var steps = Steps.Select(s => s.Step).ToList();
        var index = SelectedStep is null ? steps.Count : steps.IndexOf(SelectedStep.Step) + 1;
        ChaserStep? last = null;
        foreach (var item in picked)
        {
            if (!Project.CanPlayInChaser(Original.Id, item.Function))
            {
                continue;
            }

            last = new ChaserStep
            {
                FunctionId = item.Function.Id,
                FadeInSeconds = template?.FadeInSeconds ?? 0,
                HoldSeconds = template?.HoldSeconds ?? ChaserStep.DefaultHoldSeconds,
                FadeOutSeconds = template?.FadeOutSeconds ?? 0,
            };
            steps.Insert(index++, last);
        }

        if (last is null)
        {
            return;
        }

        RebuildSteps(steps, last);
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStep))]
    private void RemoveStep()
    {
        if (SelectedStep is not { } row)
        {
            return;
        }

        var steps = Steps.Select(s => s.Step).ToList();
        var index = steps.IndexOf(row.Step);
        steps.RemoveAt(index);
        RebuildSteps(steps, steps.Count == 0 ? null : steps[Math.Min(index, steps.Count - 1)]);
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStep))]
    private void DuplicateStep()
    {
        if (SelectedStep is not { } row)
        {
            return;
        }

        var steps = Steps.Select(s => s.Step).ToList();
        var copy = row.Step.Clone();
        steps.Insert(steps.IndexOf(row.Step) + 1, copy);
        RebuildSteps(steps, copy);
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveStepUp))]
    private void MoveStepUp() => MoveStep(-1);

    [RelayCommand(CanExecute = nameof(CanMoveStepDown))]
    private void MoveStepDown() => MoveStep(1);

    private bool HasSelectedStep => SelectedStep is not null;

    private bool CanMoveStepUp => SelectedStep is { } row && Steps.IndexOf(row) > 0;

    private bool CanMoveStepDown => SelectedStep is { } row && Steps.IndexOf(row) is var i && i >= 0 && i < Steps.Count - 1;

    private void MoveStep(int offset)
    {
        if (SelectedStep is not { } row)
        {
            return;
        }

        var steps = Steps.Select(s => s.Step).ToList();
        FunctionEditing.MoveItem(steps, steps.IndexOf(row.Step), offset);
        RebuildSteps(steps, row.Step);
        OnStructureChanged();
    }

    private void RebuildSteps(IReadOnlyList<ChaserStep> steps, ChaserStep? select)
    {
        Steps.Clear();
        ChaserStepRowViewModel? selected = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var row = new ChaserStepRowViewModel(steps[i], Project.FindFunction(steps[i].FunctionId), i + 1, OnRowTimingChanged);
            Steps.Add(row);
            if (ReferenceEquals(steps[i], select))
            {
                selected = row;
            }
        }

        SelectedStep = selected;
        MoveStepUpCommand.NotifyCanExecuteChanged();
        MoveStepDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoSteps));
        OnPropertyChanged(nameof(StepCountText));
        OnPropertyChanged(nameof(CycleText));
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

            var item = new FunctionPickerItem(function);
            if (text.Length == 0
                || item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.TypeName.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                PickerItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasNoPickerItems));
    }

    private void OnRowTimingChanged(StepTimingRowViewModel row) => OnTimingChanged();

    private void OnStructureChanged() => MarkDirty();

    private void OnTimingChanged()
    {
        OnPropertyChanged(nameof(CycleText));
        MarkDirty();
    }
}
