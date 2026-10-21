using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Sequence editor: the fixtures (ordered list with an Add flyout), the step grid (name, fade in, hold, fade out) and,
/// for the selected step, the same channel fader banks as the scene editor's Channels tab — every channel of every
/// fixture, 0–255, resolved from the profile mode by channel key. Works on a draft copy until Save.
/// </summary>
public sealed partial class SequenceEditorViewModel : FunctionEditorViewModel
{
    private SequenceFunction _draft = new();
    private SceneFunction _stepScene = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStepValues), nameof(SelectedStepTitle))]
    [NotifyCanExecuteChangedFor(nameof(RemoveStepCommand), nameof(DuplicateStepCommand), nameof(MoveStepUpCommand), nameof(MoveStepDownCommand))]
    private SequenceStepRowViewModel? _selectedStep;

    [ObservableProperty]
    private RunOrder _runOrder;

    [ObservableProperty]
    private RunDirection _direction;

    public SequenceEditorViewModel(Project project, SequenceFunction sequence, LightClipboardService clipboard)
        : base(project, sequence)
    {
        Fixtures = new FixtureListEditorViewModel(project, OnFixturesChanged);
        Channels = new SceneChannelsTabViewModel(SetChannelTypeOnSelected, clipboard);
        LoadFromOriginal();
    }

    public FixtureListEditorViewModel Fixtures { get; }

    public override void RefreshPatch() => Fixtures.RefreshPatch();

    /// <summary>Fader banks of the selected step (the scene editor's Channels tab, reused).</summary>
    public SceneChannelsTabViewModel Channels { get; }

    public ObservableCollection<SequenceStepRowViewModel> Steps { get; } = [];

    public IReadOnlyList<EditorOption<RunOrder>> RunOrders => EditorTiming.RunOrders;

    public IReadOnlyList<EditorOption<RunDirection>> Directions => EditorTiming.Directions;

    public bool HasNoSteps => Steps.Count == 0;

    public bool HasSelectedStepValues => SelectedStep is not null;

    public bool HasNoSelectedStep => SelectedStep is null;

    /// <summary>"Step 2 · Intro" (just "Step 2" when unnamed): header of the values panel.</summary>
    public string SelectedStepTitle => SelectedStep is not { } step
        ? "Step values"
        : step.Name == $"Step {step.Number}" ? step.Name : $"Step {step.Number} · {step.Name}";

    /// <summary>Hint shown when no step is selected.</summary>
    public string NoStepHint => Steps.Count == 0
        ? (Fixtures.HasNoItems ? "Add fixtures first, then add steps. Each step sets its own channel values." : "Click Add step to make the first step.")
        : "Select a step on the left to set its channel values.";

    public string CycleText => EditorTiming.CycleText(Steps.Count, _draft.CycleSeconds(), RunOrder);

    public string StepCountText => Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";

    private SequenceFunction Sequence => (SequenceFunction)Original;

    protected override void LoadDraft()
    {
        _draft = (SequenceFunction)Sequence.CloneAsNew();
        RunOrder = _draft.RunOrder;
        Direction = _draft.Direction;
        Fixtures.Load(_draft.FixtureIds);
        RebuildSteps(_draft.Steps.FirstOrDefault());
    }

    protected override void ApplyDraft()
    {
        Sequence.FixtureIds = [.. _draft.FixtureIds];
        Sequence.Steps = _draft.Steps.Select(s => s.Clone()).ToList();
        Sequence.RunOrder = RunOrder;
        Sequence.Direction = Direction;
    }

    partial void OnRunOrderChanged(RunOrder value)
    {
        _draft.RunOrder = value;
        OnTimingChanged();
    }

    partial void OnDirectionChanged(RunDirection value)
    {
        _draft.Direction = value;
        OnTimingChanged();
    }

    partial void OnSelectedStepChanged(SequenceStepRowViewModel? value) => LoadStepValues();

    [RelayCommand]
    private void AddStep()
    {
        var step = _draft.AddSequenceStep(Project);
        RebuildSteps(step);
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStepValues))]
    private void DuplicateStep()
    {
        if (SelectedStep is not { } row)
        {
            return;
        }

        var copy = _draft.AddSequenceStep(Project, row.Step);
        RebuildSteps(copy);
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStepValues))]
    private void RemoveStep()
    {
        if (SelectedStep is not { } row)
        {
            return;
        }

        var index = _draft.Steps.IndexOf(row.Step);
        _draft.Steps.RemoveAt(index);
        RebuildSteps(_draft.Steps.Count == 0 ? null : _draft.Steps[Math.Min(index, _draft.Steps.Count - 1)]);
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanMoveStepUp))]
    private void MoveStepUp() => MoveStep(-1);

    [RelayCommand(CanExecute = nameof(CanMoveStepDown))]
    private void MoveStepDown() => MoveStep(1);

    private bool CanMoveStepUp => SelectedStep is { } row && _draft.Steps.IndexOf(row.Step) > 0;

    private bool CanMoveStepDown => SelectedStep is { } row && _draft.Steps.IndexOf(row.Step) is var i && i >= 0 && i < _draft.Steps.Count - 1;

    private void MoveStep(int offset)
    {
        if (SelectedStep is not { } row)
        {
            return;
        }

        FunctionEditing.MoveItem(_draft.Steps, _draft.Steps.IndexOf(row.Step), offset);
        RebuildSteps(row.Step);
        MarkDirty();
    }

    private void RebuildSteps(SequenceStep? select)
    {
        Steps.Clear();
        SequenceStepRowViewModel? selected = null;
        for (var i = 0; i < _draft.Steps.Count; i++)
        {
            var row = new SequenceStepRowViewModel(_draft.Steps[i], i + 1, OnRowChanged);
            Steps.Add(row);
            if (ReferenceEquals(_draft.Steps[i], select))
            {
                selected = row;
            }
        }

        if (ReferenceEquals(SelectedStep, selected))
        {
            LoadStepValues();
        }
        else
        {
            SelectedStep = selected;
        }

        MoveStepUpCommand.NotifyCanExecuteChanged();
        MoveStepDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoSteps));
        OnPropertyChanged(nameof(HasNoSelectedStep));
        OnPropertyChanged(nameof(StepCountText));
        OnPropertyChanged(nameof(CycleText));
        OnPropertyChanged(nameof(NoStepHint));
        OnPropertyChanged(nameof(SelectedStepTitle));
    }

    private void OnFixturesChanged(IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed)
    {
        foreach (var id in removed)
        {
            _draft.RemoveSequenceFixture(id);
        }

        foreach (var id in added)
        {
            if (Project.FindFixture(id) is { } fixture)
            {
                _draft.AddSequenceFixture(Project, fixture);
            }
        }

        // Keep the list's order (it also covers reordering).
        _draft.FixtureIds = [.. Fixtures.FixtureIds];
        LoadStepValues();
        OnPropertyChanged(nameof(NoStepHint));
        MarkDirty();
    }

    /// <summary>Shows the selected step's values as fader banks (sequence fixture order), keeping the light selection.</summary>
    private void LoadStepValues()
    {
        OnPropertyChanged(nameof(HasNoSelectedStep));
        var previouslySelected = Channels.Fixtures.Where(b => b.IsSelected).Select(b => b.FixtureId).ToHashSet();
        var hadBanks = Channels.Fixtures.Count > 0;
        if (SelectedStep is not { } row)
        {
            _stepScene = new SceneFunction();
            Channels.Reset([]);
            return;
        }

        _stepScene = row.Step.AsScene();

        // Lookup tables: a step over hundreds of lights read through per-light/per-channel scans was quadratic.
        var fixtures = new Dictionary<Guid, Fixture>(Project.Fixtures.Count);
        foreach (var fixture in Project.Fixtures)
        {
            fixtures.TryAdd(fixture.Id, fixture);
        }

        var values = _stepScene.IndexValues();
        var banks = new List<SceneFixtureTabViewModel>();
        foreach (var fixtureId in _draft.FixtureIds)
        {
            if (fixtures.TryGetValue(fixtureId, out var fixture))
            {
                banks.Add(CreateBank(fixture, values));
            }
        }

        Channels.Reset(banks);
        if (hadBanks && banks.Any(b => previouslySelected.Contains(b.FixtureId)))
        {
            foreach (var bank in banks)
            {
                bank.SetSelectedSilently(previouslySelected.Contains(bank.FixtureId));
            }

            Channels.AfterFixturesChanged();
        }
    }

    private SceneFixtureTabViewModel CreateBank(Fixture fixture, IReadOnlyDictionary<(Guid FixtureId, string ChannelKey), byte> values)
    {
        var profile = Project.FindProfile(fixture.ProfileId);
        var channels = Project.ResolveFixtureChannels(fixture);
        var mode = profile is null ? "missing profile" : fixture.ModeName;
        var range = channels.Count <= 1
            ? $"U{fixture.Universe} · {fixture.StartAddress}"
            : $"U{fixture.Universe} · {fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, channels.Count)}";
        var bank = new SceneFixtureTabViewModel(fixture, $"{range} · {mode}", SetAllChannelsIncluded);
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            byte? stored = values.TryGetValue((fixture.Id, channel.Key), out var value) ? value : null;
            bank.Channels.Add(new SceneChannelFaderViewModel(
                fixture.Id,
                channel,
                number: i + 1,
                dmxAddress: fixture.StartAddress + i,
                stored ?? channel.Default,
                stored is not null,
                OnFaderChanged));
        }

        return bank;
    }

    private void OnFaderChanged(SceneChannelFaderViewModel fader)
    {
        _stepScene.SetChannelIncluded(fader.FixtureId, fader.Channel, fader.IsIncluded, fader.IsIncluded ? fader.ByteValue : null);
        if (Channels.BulkType?.Type == fader.Type)
        {
            Channels.RefreshBulkValue();
        }

        MarkDirty();
    }

    private void SetAllChannelsIncluded(SceneFixtureTabViewModel bank, bool included)
    {
        foreach (var fader in bank.Channels)
        {
            _stepScene.SetChannelIncluded(bank.FixtureId, fader.Channel, included, included ? fader.ByteValue : null);
            fader.SetIncludedSilently(included);
        }

        Channels.RefreshBulkValue();
        MarkDirty();
    }

    /// <summary>"Set on selected lights": one channel type (resolved per profile) on every selected light of this step.</summary>
    private void SetChannelTypeOnSelected(ChannelType type, byte value)
    {
        var changed = false;
        foreach (var bank in Channels.SelectedBanks)
        {
            if (_stepScene.SetChannelType(Project, bank.Fixture, type, value) == 0)
            {
                continue;
            }

            changed = true;
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    fader.SetIncludedSilently(true);
                    fader.SetValueSilently(value);
                }
            }
        }

        if (changed)
        {
            MarkDirty();
        }
    }

    private void OnRowChanged(StepTimingRowViewModel row)
    {
        OnPropertyChanged(nameof(SelectedStepTitle));
        OnTimingChanged();
    }

    private void OnTimingChanged()
    {
        OnPropertyChanged(nameof(CycleText));
        MarkDirty();
    }
}
