using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Sequence editor: a chaser of mini-scenes. Three tabs like the scene editor:
/// <list type="bullet">
/// <item><b>Lights</b> (<see cref="Fixtures"/>): the lights every step uses (Choose lights dialog).</item>
/// <item><b>Steps</b>: the step list (number, colour swatches, name, timing; multi-select) and, for the selected step,
/// its values: <b>Colours</b> (colour presets, Intensity and colour sliders for the selected lights) or
/// <b>All channels</b> (the scene editor's Channels tab, reused: every channel 0–255).</item>
/// <item><b>Playback</b>: run order, direction, the same timing for every step, cycle length.</item>
/// </list>
/// Works on a draft copy until Save. While editing, the live preview shows the selected step held (or the whole
/// sequence running, <see cref="PreviewWholeSequence"/>). Every channel is resolved through the fixture's profile mode.
/// </summary>
public sealed partial class SequenceEditorViewModel : FunctionEditorViewModel
{
    /// <summary>Tab indexes (<see cref="SelectedTabIndex"/>).</summary>
    public const int LightsTab = 0;

    public const int StepsTab = 1;

    public const int PlaybackTab = 2;

    /// <summary>Slider rows of the Colours view, in this order, when a selected light has the type.</summary>
    private static readonly ChannelType[] LevelTypes =
    [
        ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV,
    ];

    private readonly Dictionary<SequenceStep, (IReadOnlyList<SequenceSwatch> Swatches, int Count)> _swatchCache = [];
    private readonly List<(int Index, SequenceStep Step)> _removed = [];
    private Dictionary<Guid, Fixture> _fixtures = [];
    private SequenceFunction _draft = new();
    private SceneFunction _stepScene = new();
    private double _timingFadeIn;
    private double _timingHold = ChaserStep.DefaultHoldSeconds;
    private double _timingFadeOut;
    private bool _rebuilding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStepValues), nameof(HasNoSelectedStep), nameof(SelectedStepTitle))]
    private SequenceStepRowViewModel? _selectedStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoop), nameof(IsPlayOnce), nameof(IsBackAndForth))]
    private RunOrder _runOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForward), nameof(IsBackward))]
    private RunDirection _direction;

    /// <summary><see cref="LightsTab"/>, <see cref="StepsTab"/> or <see cref="PlaybackTab"/>.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>0 = Colours (simple), 1 = All channels (every channel's fader).</summary>
    [ObservableProperty]
    private int _valuesViewIndex;

    /// <summary>False: the lights show the selected step, held. True: the whole sequence runs on the lights.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewSelectedStep))]
    private bool _previewWholeSequence;

    /// <summary>"Removed step 3." while Undo can bring removed steps back; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndo))]
    [NotifyCanExecuteChangedFor(nameof(UndoRemoveCommand))]
    private string? _undoText;

    public SequenceEditorViewModel(Project project, SequenceFunction sequence, LightClipboardService clipboard)
        : base(project, sequence)
    {
        Fixtures = new FixtureListEditorViewModel(project, OnFixturesChanged);
        Channels = new SceneChannelsTabViewModel(SetChannelTypeOnSelected, clipboard);
        Channels.PropertyChanged += OnChannelsChanged;
        LoadFromOriginal();
        SelectedTabIndex = _draft.FixtureIds.Count == 0 ? LightsTab : StepsTab;
    }

    /// <summary>The lights of the sequence (Lights tab).</summary>
    public FixtureListEditorViewModel Fixtures { get; }

    /// <summary>All channels view of the selected step (the scene editor's Channels tab, reused).</summary>
    public SceneChannelsTabViewModel Channels { get; }

    public ObservableCollection<SequenceStepRowViewModel> Steps { get; } = [];

    /// <summary>The selected step's lights for the Colours view (selection shared with <see cref="Channels"/>).</summary>
    public ObservableCollection<SequenceLightRowViewModel> Lights { get; } = [];

    /// <summary>Intensity and colour sliders for the selected lights (only the types they have).</summary>
    public ObservableCollection<SequenceLevelRowViewModel> Levels { get; } = [];

    /// <summary>The same one-click colours as the colour editors.</summary>
    public IReadOnlyList<SceneEditorColorPreset> Presets => SceneEditorColorPreset.All;

    public bool HasLights => _draft.FixtureIds.Count > 0;

    public bool HasNoLights => _draft.FixtureIds.Count == 0;

    public bool HasNoSteps => Steps.Count == 0;

    /// <summary>The sequence has lights but no step yet (the step list's empty state).</summary>
    public bool ShowNoStepsHint => HasLights && Steps.Count == 0;

    public bool HasSelectedStepValues => SelectedStep is not null;

    public bool HasNoSelectedStep => SelectedStep is null;

    public bool HasSelectedLights => Channels.SelectedCount > 0;

    public bool HasNoSelectedLights => Channels.SelectedCount == 0 && Channels.Fixtures.Count > 0;

    public bool HasLevels => Levels.Count > 0;

    public bool HasUndo => UndoText is not null;

    public bool PreviewSelectedStep
    {
        get => !PreviewWholeSequence;
        set => PreviewWholeSequence = !value;
    }

    /// <summary>"Step 2 · Intro" for the values panel header.</summary>
    public string SelectedStepTitle => SelectedStep?.Title ?? "No step selected";

    /// <summary>Hint in the values panel when no step is selected.</summary>
    public string NoStepHint => Steps.Count == 0
        ? "Add a step, then set its colours here."
        : "Select a step on the left to set its colours.";

    /// <summary>"2 of 4 lights selected".</summary>
    public string SelectedLightsText => $"{Channels.SelectedCount} of {Channels.Fixtures.Count} selected";

    public string CycleText => EditorTiming.CycleText(Steps.Count, _draft.CycleSeconds(), RunOrder);

    public string StepCountText => Steps.Count == 1 ? "1 step" : $"{Steps.Count} steps";

    /// <summary>"3 steps selected" for the timing strip.</summary>
    public string SelectedStepsText
    {
        get
        {
            var count = SelectedRows().Count;
            return count == 1 ? "1 step selected" : $"{count} steps selected";
        }
    }

    /// <summary>"4 lights" for the Lights tab hint.</summary>
    public string LightCountText => _draft.FixtureIds.Count == 1 ? "1 light" : $"{_draft.FixtureIds.Count} lights";

    // ───────────────────────────── Playback (radio buttons) ─────────────────────────────

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

    // ───────────────────────────── Timing boxes (apply to selected / all) ─────────────────────────────

    /// <summary>Fade-in seconds typed for "Apply timing"; invalid text reverts.</summary>
    public string TimingFadeInText
    {
        get => StepTimingRowViewModel.Format(_timingFadeIn);
        set => SetTiming(ref _timingFadeIn, value, nameof(TimingFadeInText));
    }

    public string TimingHoldText
    {
        get => StepTimingRowViewModel.Format(_timingHold);
        set => SetTiming(ref _timingHold, value, nameof(TimingHoldText));
    }

    public string TimingFadeOutText
    {
        get => StepTimingRowViewModel.Format(_timingFadeOut);
        set => SetTiming(ref _timingFadeOut, value, nameof(TimingFadeOutText));
    }

    private SequenceFunction Sequence => (SequenceFunction)Target;

    public override void RefreshPatch()
    {
        Fixtures.RefreshPatch();
        RebuildFixtureLookup();
        _swatchCache.Clear();
        RefreshAllSwatches();
        LoadStepValues();
    }

    // ───────────────────────────── Draft ─────────────────────────────

    protected override void LoadDraft()
    {
        _draft = (SequenceFunction)Sequence.CloneAsNew();
        RunOrder = _draft.RunOrder;
        Direction = _draft.Direction;
        Fixtures.Load(_draft.FixtureIds);
        RebuildFixtureLookup();
        _swatchCache.Clear();
        _removed.Clear();
        UndoText = null;
        var first = _draft.Steps.FirstOrDefault();
        if (first is not null)
        {
            _timingFadeIn = first.FadeInSeconds;
            _timingHold = first.HoldSeconds;
            _timingFadeOut = first.FadeOutSeconds;
        }

        OnPropertyChanged(nameof(TimingFadeInText));
        OnPropertyChanged(nameof(TimingHoldText));
        OnPropertyChanged(nameof(TimingFadeOutText));
        RebuildSteps(first is null ? [] : [first], first);
        OnLightsChanged();
    }

    /// <summary>
    /// Writes the draft. For the live preview (<see cref="FunctionEditorViewModel.BuildPreviewFunction"/>) of the
    /// selected step, the copy gets only that step, held without fades and looping, so the lights show it steadily.
    /// </summary>
    protected override void ApplyDraft()
    {
        var target = Sequence;
        target.FixtureIds = [.. _draft.FixtureIds];
        if (!ReferenceEquals(target, Original) && !PreviewWholeSequence && SelectedStep is { } row)
        {
            target.Steps = [SequenceStepEditing.HeldCopy(row.Step)];
            target.RunOrder = RunOrder.Loop;
            target.Direction = RunDirection.Forward;
            return;
        }

        target.Steps = _draft.Steps.Select(s => s.Clone()).ToList();
        target.RunOrder = RunOrder;
        target.Direction = Direction;
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

    partial void OnSelectedStepChanged(SequenceStepRowViewModel? value)
    {
        if (_rebuilding)
        {
            return;
        }

        LoadStepValues();
        NotifySelectionCommands();
        if (!PreviewWholeSequence)
        {
            RefreshPreview();
        }
    }

    partial void OnPreviewWholeSequenceChanged(bool value) => RefreshPreview();

    // ───────────────────────────── Step commands ─────────────────────────────

    /// <summary>
    /// Adds a copy of the selected step right after it (values and timing), so a pattern is built by changing one
    /// thing per step. With no step selected: a copy of the last step; with no steps: a blank step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasLights))]
    private void AddStep()
    {
        var source = SelectedStep?.Step ?? _draft.Steps.LastOrDefault();
        if (source is null)
        {
            AddBlankStep();
            return;
        }

        var copy = SequenceStepEditing.CopyOf(source);
        _draft.Steps.Insert(_draft.Steps.IndexOf(source) + 1, copy);
        AfterStructureChanged([copy], copy);
    }

    /// <summary>Adds a step with every channel at its profile default, after the selected step, with its timing.</summary>
    [RelayCommand(CanExecute = nameof(HasLights))]
    private void AddBlankStep()
    {
        var after = SelectedStep?.Step;
        var step = SequenceStepEditing.CreateBlank(_draft, Project, after ?? _draft.Steps.LastOrDefault());
        var index = after is null ? _draft.Steps.Count : _draft.Steps.IndexOf(after) + 1;
        _draft.Steps.Insert(index, step);
        AfterStructureChanged([step], step);
    }

    /// <summary>Copies every selected step (in order) and puts the copies right after the last selected one.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRows))]
    private void DuplicateSteps()
    {
        var rows = SelectedRows();
        if (rows.Count == 0)
        {
            return;
        }

        var insertAt = rows.Max(r => _draft.Steps.IndexOf(r.Step)) + 1;
        var copies = rows.Select(r => SequenceStepEditing.CopyOf(r.Step)).ToList();
        _draft.Steps.InsertRange(insertAt, copies);
        AfterStructureChanged(copies, copies[0]);
    }

    /// <summary>Removes every selected step; <see cref="UndoRemoveCommand"/> brings them back.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRows))]
    private void RemoveSteps()
    {
        var rows = SelectedRows();
        if (rows.Count == 0)
        {
            return;
        }

        var removed = rows
            .Select(r => (Index: _draft.Steps.IndexOf(r.Step), r.Step))
            .Where(r => r.Index >= 0)
            .OrderBy(r => r.Index)
            .ToList();
        var firstIndex = removed[0].Index;
        foreach (var (_, step) in removed)
        {
            _draft.Steps.Remove(step);
        }

        _removed.Clear();
        _removed.AddRange(removed);
        UndoText = removed.Count == 1
            ? $"Removed step {removed[0].Index + 1}."
            : $"Removed {removed.Count} steps.";

        var next = _draft.Steps.Count == 0 ? null : _draft.Steps[Math.Min(firstIndex, _draft.Steps.Count - 1)];
        AfterStructureChanged(next is null ? [] : [next], next, keepUndo: true);
    }

    /// <summary>Puts the last removed steps back where they were.</summary>
    [RelayCommand(CanExecute = nameof(HasUndo))]
    private void UndoRemove()
    {
        if (_removed.Count == 0)
        {
            UndoText = null;
            return;
        }

        var restored = new List<SequenceStep>();
        foreach (var (index, step) in _removed)
        {
            _draft.Steps.Insert(Math.Min(index, _draft.Steps.Count), step);
            restored.Add(step);
        }

        AfterStructureChanged(restored, restored[0]);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveStepsUp() => MoveSteps(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveStepsDown() => MoveSteps(1);

    /// <summary>Gives the selected steps the fade in / hold / fade out typed in the timing boxes.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRows))]
    private void ApplyTimingToSelected() => ApplyTiming(SelectedRows());

    /// <summary>"Same time for all steps": every step gets the timing boxes' values.</summary>
    [RelayCommand(CanExecute = nameof(HasAnySteps))]
    private void ApplyTimingToAll() => ApplyTiming(Steps.ToList());

    // ───────────────────────────── Colours view commands ─────────────────────────────

    /// <summary>Sets every selected light of the selected step to the preset colour.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelectedLights))]
    private void ApplyPreset(SceneEditorColorPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        EditSelectedLights(fixture => SequenceStepEditing.ApplyColor(_stepScene, Project, fixture, preset));
    }

    /// <summary>Turns every selected light of the selected step off (colours and intensity at 0).</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelectedLights))]
    private void LightsOff() => EditSelectedLights(fixture => SequenceStepEditing.SetOff(_stepScene, Project, fixture));

    [RelayCommand]
    private void SelectAllLights() => Channels.SelectAllCommand.Execute(null);

    [RelayCommand]
    private void SelectNoLights() => Channels.ClearSelectionCommand.Execute(null);

    [RelayCommand]
    private void GoToLights() => SelectedTabIndex = LightsTab;

    [RelayCommand]
    private void GoToSteps() => SelectedTabIndex = StepsTab;

    private bool HasSelectedRows() => SelectedRows().Count > 0;

    private bool HasAnySteps() => Steps.Count > 0;

    private bool CanEditSelectedLights() => SelectedStep is not null && Channels.SelectedCount > 0;

    private bool CanMoveUp()
    {
        var rows = SelectedRows();
        return rows.Count > 0 && rows[0].Number > 1;
    }

    private bool CanMoveDown()
    {
        var rows = SelectedRows();
        return rows.Count > 0 && rows[^1].Number < Steps.Count;
    }

    /// <summary>The selected rows in list order; the current row when nothing is marked selected.</summary>
    internal List<SequenceStepRowViewModel> SelectedRows()
    {
        var rows = new List<SequenceStepRowViewModel>();
        foreach (var row in Steps)
        {
            if (row.IsSelected)
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0 && SelectedStep is { } current && Steps.Contains(current))
        {
            rows.Add(current);
        }

        return rows;
    }

    private void MoveSteps(int offset)
    {
        var rows = SelectedRows();
        var selected = rows.Select(r => r.Step).ToHashSet();
        if (!SequenceStepEditing.MoveItems(_draft.Steps, selected, offset))
        {
            return;
        }

        AfterStructureChanged(rows.Select(r => r.Step).ToList(), SelectedStep?.Step);
    }

    private void ApplyTiming(IReadOnlyList<SequenceStepRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        SequenceStepEditing.ApplyTiming(rows.Select(r => r.Step), _timingFadeIn, _timingHold, _timingFadeOut);
        foreach (var row in rows)
        {
            row.RefreshTiming();
        }

        OnTimingChanged();
    }

    private void SetTiming(ref double field, string? text, string propertyName)
    {
        if (FunctionEditing.TryParseNumber(text, 0, FunctionEditing.MaxSeconds, out var seconds))
        {
            field = seconds;
        }

        OnPropertyChanged(propertyName);
    }

    private void SetRunOrder(bool isChecked, RunOrder order)
    {
        if (isChecked)
        {
            RunOrder = order;
        }
    }

    /// <summary>After add / remove / move / undo: rebuild the list, select <paramref name="select"/>, mark changed.</summary>
    private void AfterStructureChanged(IReadOnlyCollection<SequenceStep> select, SequenceStep? current, bool keepUndo = false)
    {
        if (!keepUndo)
        {
            _removed.Clear();
            UndoText = null;
        }

        RebuildSteps(select, current);
        MarkDirty();
    }

    private void RebuildSteps(IReadOnlyCollection<SequenceStep> select, SequenceStep? current)
    {
        _rebuilding = true;
        try
        {
            var selected = select.ToHashSet();
            SequenceStepRowViewModel? currentRow = null;
            Steps.Clear();
            for (var i = 0; i < _draft.Steps.Count; i++)
            {
                var step = _draft.Steps[i];
                var row = new SequenceStepRowViewModel(step, i + 1, OnRowChanged, OnRowSelectionChanged);
                row.SetSelectedSilently(selected.Contains(step));
                ApplySwatches(row);
                Steps.Add(row);
                if (ReferenceEquals(step, current))
                {
                    currentRow = row;
                }
            }

            SelectedStep = currentRow;
        }
        finally
        {
            _rebuilding = false;
        }

        // Once, after the list settled (the grid may clear its selection while the rows are replaced).
        LoadStepValues();

        NotifySelectionCommands();
        AddStepCommand.NotifyCanExecuteChanged();
        AddBlankStepCommand.NotifyCanExecuteChanged();
        ApplyTimingToAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoSteps));
        OnPropertyChanged(nameof(ShowNoStepsHint));
        OnPropertyChanged(nameof(StepCountText));
        OnPropertyChanged(nameof(CycleText));
        OnPropertyChanged(nameof(NoStepHint));
        OnPropertyChanged(nameof(SelectedStepTitle));
    }

    private void OnRowSelectionChanged(SequenceStepRowViewModel row)
    {
        if (!_rebuilding)
        {
            NotifySelectionCommands();
        }
    }

    private void NotifySelectionCommands()
    {
        DuplicateStepsCommand.NotifyCanExecuteChanged();
        RemoveStepsCommand.NotifyCanExecuteChanged();
        MoveStepsUpCommand.NotifyCanExecuteChanged();
        MoveStepsDownCommand.NotifyCanExecuteChanged();
        ApplyTimingToSelectedCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        LightsOffCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedStepsText));
    }

    // ───────────────────────────── Lights ─────────────────────────────

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

        // Removed steps refer to the old light set; they cannot come back.
        _removed.Clear();
        UndoText = null;
        RebuildFixtureLookup();
        _swatchCache.Clear();
        RefreshAllSwatches();
        LoadStepValues();
        OnLightsChanged();
        MarkDirty();
    }

    private void OnLightsChanged()
    {
        OnPropertyChanged(nameof(HasLights));
        OnPropertyChanged(nameof(HasNoLights));
        OnPropertyChanged(nameof(ShowNoStepsHint));
        OnPropertyChanged(nameof(LightCountText));
        OnPropertyChanged(nameof(NoStepHint));
        AddStepCommand.NotifyCanExecuteChanged();
        AddBlankStepCommand.NotifyCanExecuteChanged();
    }

    private void RebuildFixtureLookup()
    {
        _fixtures = new Dictionary<Guid, Fixture>(Project.Fixtures.Count);
        foreach (var fixture in Project.Fixtures)
        {
            _fixtures.TryAdd(fixture.Id, fixture);
        }
    }

    // ───────────────────────────── Values of the selected step ─────────────────────────────

    /// <summary>Shows the selected step's lights (Colours view) and fader banks (All channels), keeping the light selection.</summary>
    private void LoadStepValues()
    {
        var previouslySelected = Channels.Fixtures.Where(b => b.IsSelected).Select(b => b.FixtureId).ToHashSet();
        var hadBanks = Channels.Fixtures.Count > 0;
        Lights.Clear();
        if (SelectedStep is not { } row)
        {
            _stepScene = new SceneFunction();
            Channels.Reset([]);
            AfterLightSelectionChanged();
            return;
        }

        _stepScene = row.Step.AsScene();
        var values = _stepScene.IndexValues();
        var banks = new List<SceneFixtureTabViewModel>();
        foreach (var fixtureId in _draft.FixtureIds)
        {
            if (_fixtures.TryGetValue(fixtureId, out var fixture))
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

        foreach (var bank in banks)
        {
            Lights.Add(new SequenceLightRowViewModel(bank, SequenceStepEditing.Swatch(bank.Header, values.PreviewColor(Project, bank.Fixture))));
        }

        AfterLightSelectionChanged();
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

    private void OnChannelsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Raised once per selection rebuild in the Channels view model.
        if (e.PropertyName == nameof(SceneChannelsTabViewModel.SelectionSummary))
        {
            AfterLightSelectionChanged();
        }
    }

    private void AfterLightSelectionChanged()
    {
        RebuildLevels();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        LightsOffCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedLights));
        OnPropertyChanged(nameof(HasNoSelectedLights));
        OnPropertyChanged(nameof(SelectedLightsText));
    }

    /// <summary>One slider per level type a selected light has, showing the first selected light's level.</summary>
    private void RebuildLevels()
    {
        Levels.Clear();
        foreach (var type in LevelTypes)
        {
            if (FirstSelectedLevel(type) is { } level)
            {
                Levels.Add(new SequenceLevelRowViewModel(type, level, SetLevelOnSelected));
            }
        }

        OnPropertyChanged(nameof(HasLevels));
    }

    private void RefreshLevelValues()
    {
        foreach (var row in Levels)
        {
            if (FirstSelectedLevel(row.Type) is { } level)
            {
                row.SetValueSilently(level);
            }
        }
    }

    private int? FirstSelectedLevel(ChannelType type)
    {
        foreach (var bank in Channels.SelectedBanks)
        {
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    return fader.ByteValue;
                }
            }
        }

        return null;
    }

    private void SetLevelOnSelected(ChannelType type, byte value)
    {
        SetChannelTypeOnSelected(type, value);
        Channels.RefreshBulkValue();
    }

    /// <summary>Runs an edit on every selected light of the step, then syncs faders, swatches and sliders.</summary>
    private void EditSelectedLights(Func<Fixture, bool> edit)
    {
        var changed = new List<Guid>();
        foreach (var bank in Channels.SelectedBanks)
        {
            if (edit(bank.Fixture))
            {
                SyncBank(bank);
                changed.Add(bank.FixtureId);
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        Channels.RefreshBulkValue();
        AfterValuesChanged(changed);
        MarkDirty();
    }

    /// <summary>Re-reads one bank's faders from the step (after an edit that bypassed them).</summary>
    private void SyncBank(SceneFixtureTabViewModel bank)
    {
        foreach (var fader in bank.Channels)
        {
            var stored = _stepScene.GetValue(bank.FixtureId, fader.Channel.Key);
            fader.SetIncludedSilently(stored is not null);
            if (stored is { } value)
            {
                fader.SetValueSilently(value);
            }
        }
    }

    private void OnFaderChanged(SceneChannelFaderViewModel fader)
    {
        _stepScene.SetChannelIncluded(fader.FixtureId, fader.Channel, fader.IsIncluded, fader.IsIncluded ? fader.ByteValue : null);
        if (Channels.BulkType?.Type == fader.Type)
        {
            Channels.RefreshBulkValue();
        }

        AfterValuesChanged([fader.FixtureId]);
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
        AfterValuesChanged([bank.FixtureId]);
        MarkDirty();
    }

    /// <summary>"Set on selected lights" (All channels view) and the Colours sliders: one channel type on every selected light.</summary>
    private void SetChannelTypeOnSelected(ChannelType type, byte value)
    {
        var changed = new List<Guid>();
        foreach (var bank in Channels.SelectedBanks)
        {
            if (_stepScene.SetChannelType(Project, bank.Fixture, type, value) == 0)
            {
                continue;
            }

            changed.Add(bank.FixtureId);
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    fader.SetIncludedSilently(true);
                    fader.SetValueSilently(value);
                }
            }
        }

        if (changed.Count > 0)
        {
            AfterValuesChanged(changed);
            MarkDirty();
        }
    }

    /// <summary>The step's values changed for these lights: update their swatches, the step row and the sliders.</summary>
    private void AfterValuesChanged(IReadOnlyCollection<Guid> fixtureIds)
    {
        foreach (var light in Lights)
        {
            if (fixtureIds.Contains(light.FixtureId))
            {
                light.SetSwatch(SequenceStepEditing.Swatch(light.Name, _stepScene.PreviewColor(Project, light.Bank.Fixture)));
            }
        }

        if (SelectedStep is { } row)
        {
            _swatchCache.Remove(row.Step);
            ApplySwatches(row);
        }

        RefreshLevelValues();
    }

    private void ApplySwatches(SequenceStepRowViewModel row)
    {
        if (!_swatchCache.TryGetValue(row.Step, out var entry))
        {
            var count = 0;
            foreach (var id in _draft.FixtureIds)
            {
                if (_fixtures.ContainsKey(id))
                {
                    count++;
                }
            }

            entry = (SequenceStepEditing.Swatches(row.Step, _draft.FixtureIds, _fixtures, Project), count);
            _swatchCache[row.Step] = entry;
        }

        row.SetSwatches(entry.Swatches, entry.Count);
    }

    private void RefreshAllSwatches()
    {
        foreach (var row in Steps)
        {
            ApplySwatches(row);
        }
    }

    // ───────────────────────────── Changes ─────────────────────────────

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

    /// <summary>
    /// Re-sends the draft to the live preview without counting as an edit (another step selected, or the preview
    /// switched between this step and the whole sequence). The base class only raises its draft event through
    /// <see cref="FunctionEditorViewModel.MarkDirty"/>, so the unsaved-changes flag is put back right after.
    /// </summary>
    private void RefreshPreview()
    {
        if (IsLoading)
        {
            return;
        }

        var wasDirty = IsDirty;
        MarkDirty();
        IsDirty = wasDirty;
    }
}
