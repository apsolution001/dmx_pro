using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Chaser editor. A chaser plays its steps one after another; each step is either
/// <list type="bullet">
/// <item>an <b>own look</b>: a hidden scene the chaser owns, whose lights and colours are set right here in the step-look
/// panel (<see cref="StepLook"/>), or</item>
/// <item>a <b>function step</b>: the chaser's own hidden copy of a function picked with "Add from function…", changed
/// with "Edit step" (that function's normal editor, shown inside this one with "← Back to chaser"), or</item>
/// <item>a <b>linked</b> step of an older project that still plays a library function; its first edit makes it the
/// chaser's own copy, so editing a step never changes a library function.</item>
/// </list>
/// The draft is the chaser's steps plus copies of every function it owns (<see cref="DraftFunctions"/>); new step
/// functions live only in the draft until Save, which commits both together (<see cref="ChaserSteps.CommitChaser"/>);
/// Revert and Don't save drop them. The live preview plays the draft, owned step functions included, either the
/// selected step held (<see cref="PreviewSelectedStep"/>) or the whole chaser. Also: multi-select, move, drag,
/// duplicate, remove with Undo, step timing ("Times for new steps", applied to the selected or all steps), run order,
/// direction and a colour strip of one cycle. List logic lives in <see cref="ChaserStepListEditing"/>.
/// </summary>
public sealed partial class ChaserEditorViewModel : FunctionEditorViewModel
{
    private readonly AppSettingsService _settings;
    private readonly LightClipboardService _clipboard;

    // Draft copies (same ids) of every function the chaser owns, recursively, plus new ones; the step-look panel and
    // inner editors never touch the project's objects before Save.
    private readonly List<LightFunction> _owned = [];

    // Ids in _owned that are new or changed since the last load / save: what Save hands to CommitChaser.
    private readonly HashSet<Guid> _changedOwned = [];

    // The project's own objects of OwnedFunctions(Original.Id) when _owned was last loaded or committed. Undo / Redo
    // replaces a changed function with a new object, so a missing or different object means the draft is stale.
    private readonly List<LightFunction> _ownedSources = [];

    // Set while the chaser's Save also saves a dirty step editor: that save is part of this one (Saved is raised once).
    private bool _savingInner;

    private double _newHoldSeconds = ChaserStep.DefaultHoldSeconds;
    private double _newFadeInSeconds;
    private double _newFadeOutSeconds;

    // Set while the editor changes many rows at once: row selection callbacks then wait for one refresh at the end.
    private bool _bulkUpdate;

    // The editor changed the selection itself: the list must follow (raised once per refresh).
    private bool _selectionResetPending;

    private ChaserStepRowViewModel? _current;

    // The step the right panel was last set up for (look panel scene, pending copy, note).
    private ChaserStepRowViewModel? _shownRow;
    private List<(int Index, ChaserStepRowViewModel Row)>? _removed;

    // A linked scene step shown in the look panel edits this own copy; it replaces the library scene on the first edit.
    private ChaserStepRowViewModel? _pendingCopyRow;
    private IReadOnlyList<LightFunction>? _pendingCopies;

    private bool _ignoreLookChanges;
    private FunctionEditorViewModel? _stepEditor;
    private int _stepEditorIndex = -1;

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

    /// <summary>False (default): the lights show the selected step, held. True: they play the whole chaser.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewSelectedStep))]
    private bool _previewWholeChaser;

    /// <summary>"Removed 2 steps." while Undo can bring them back; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndo))]
    [NotifyCanExecuteChangedFor(nameof(UndoRemoveCommand))]
    private string? _undoText;

    /// <summary>One-line note after a linked step became the chaser's own copy; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStepNote))]
    private string? _stepNote;

    public ChaserEditorViewModel(Project project, ChaserFunction chaser, AppSettingsService settings, LightClipboardService clipboard)
        : base(project, chaser)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clipboard);
        _settings = settings;
        _clipboard = clipboard;
        PropertyChanged += OnOwnPropertyChanged;
        LoadFromOriginal();
    }

    /// <summary>
    /// The editor changed which steps are selected (add, duplicate, remove, undo, select all): the list view makes its
    /// selection match the rows' <see cref="ChaserStepRowViewModel.IsSelected"/>.
    /// </summary>
    public event EventHandler? SelectionReset;

    /// <summary>
    /// Prompts (unsaved changes before "Edit step" / "Back", the "Save as function" name). Set by the view (editors are
    /// created without DI) or by tests. While null, those actions do nothing when they would have to ask.
    /// </summary>
    public IDialogService? Dialogs { get; set; }

    public ObservableCollection<ChaserStepRowViewModel> Steps { get; } = [];

    /// <summary>The steps of one cycle in the order they play (a step can appear twice when going back and forth).</summary>
    public ObservableCollection<ChaserStepRowViewModel> PlayOrderSteps { get; } = [];

    /// <summary>Functions "Add from function…" offers (filtered by <see cref="PickerSearch"/>).</summary>
    public ObservableCollection<ChaserPickerItem> PickerItems { get; } = [];

    /// <summary>The picker has nothing to offer at all (no functions yet, or only ones that would play this chaser).</summary>
    public bool HasNoCandidates { get; private set; }

    /// <summary>There are functions to add, but none matches the search.</summary>
    public bool HasNoMatches { get; private set; }

    /// <summary>"No matches for "xyz"."</summary>
    public string NoMatchesText => $"No matches for \"{PickerSearch?.Trim()}\".";

    public bool HasNoSteps => Steps.Count == 0;

    public bool HasSteps => Steps.Count > 0;

    public int SelectedCount => Steps.Count(s => s.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public bool HasUndo => UndoText is not null;

    public bool HasStepNote => StepNote is not null;

    /// <summary>The step the right-hand panel shows (the selected one; with several selected, the first one picked).</summary>
    public ChaserStepRowViewModel? CurrentStep => _current;

    /// <summary>The step-look panel (created when the first own-look step is shown).</summary>
    public ChaserStepLookViewModel? StepLook { get; private set; }

    /// <summary>The right panel shows the step-look panel (an own look, or a linked scene).</summary>
    public bool IsLookPanelVisible => _current is { HasLook: true } && StepLook is not null;

    /// <summary>The right panel shows a function step (kind, summary, Edit step).</summary>
    public bool IsFunctionPanelVisible => _current is { HasLook: false };

    /// <summary>No step is selected: the right panel says how to start.</summary>
    public bool IsNoStepPanelVisible => _current is null;

    /// <summary>"Step 3 · Red pulse".</summary>
    public string CurrentStepTitle => _current is { } row ? $"Step {row.Number} · {row.FunctionName}" : "No step selected";

    /// <summary>Linked step: what happens on its first edit. Null for other steps.</summary>
    public string? LinkedHint => _current is { IsLinked: true } row
        ? $"Linked: plays \"{row.FunctionName}\" from the Functions list. Your first change makes it this chaser's own copy; the original stays unchanged."
        : null;

    public bool HasLinkedHint => LinkedHint is not null;

    /// <summary>The step's function editor shown inside this one ("Edit step"); null while the chaser itself is shown.</summary>
    public FunctionEditorViewModel? StepEditor => _stepEditor;

    public override FunctionEditorViewModel? InnerEditor => _stepEditor;

    /// <summary>"← Back to chaser "Party"".</summary>
    public string BackToChaserText => $"← Back to chaser \"{Original.Name}\"";

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

    public bool PreviewSelectedStep
    {
        get => !PreviewWholeChaser;
        set => PreviewWholeChaser = !value;
    }

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

    /// <summary>Hold for new steps and "Apply to …", seconds (0–3600); loaded from the first selected step.</summary>
    public double? NewHoldSeconds
    {
        get => _newHoldSeconds;
        set => SetTimingField(ref _newHoldSeconds, value);
    }

    /// <summary>Fade in for new steps and "Apply to …", seconds.</summary>
    public double? NewFadeInSeconds
    {
        get => _newFadeInSeconds;
        set => SetTimingField(ref _newFadeInSeconds, value);
    }

    /// <summary>Fade out for new steps and "Apply to …", seconds.</summary>
    public double? NewFadeOutSeconds
    {
        get => _newFadeOutSeconds;
        set => SetTimingField(ref _newFadeOutSeconds, value);
    }

    /// <summary>The draft's step functions (owned copies, new ones included) for the live preview.</summary>
    protected override IReadOnlyList<LightFunction> DraftFunctions => _owned;

    private ChaserFunction Chaser => (ChaserFunction)Target;

    private List<int> SelectedIndexes => Steps.Select((row, i) => (row, i)).Where(x => x.row.IsSelected).Select(x => x.i).ToList();

    /// <summary>
    /// The list's selection changed (click, Ctrl / Shift + click, keyboard): the rows follow it.
    /// <paramref name="current"/> is the step the right panel should show when it is selected.
    /// </summary>
    public void SyncSelection(IEnumerable<ChaserStepRowViewModel> selected, ChaserStepRowViewModel? current)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var keep = selected.ToHashSet();
        RunBulk(() =>
        {
            foreach (var row in Steps)
            {
                row.IsSelected = keep.Contains(row);
            }
        });

        if (current is { IsSelected: true })
        {
            _current = current;
        }

        OnSelectionChanged();
    }

    /// <summary>
    /// "Save as function" for the selected step: adds a library copy of what the step plays (its draft look included)
    /// to the Functions list under <paramref name="name"/>. The step keeps its own copy; the chaser draft is unchanged.
    /// Returns the new function, or null when no step with a function is selected.
    /// </summary>
    public LightFunction? SaveStepAsFunction(string name)
    {
        if (_current?.Function is not { } source)
        {
            return null;
        }

        var ownedIds = _owned.Select(f => f.Id).ToHashSet();
        var pool = _owned.Concat(Project.Functions.Where(f => !ownedIds.Contains(f.Id))).ToList();
        var copies = ChaserSteps.CloneWithOwned(source, pool);
        var copy = copies[0];
        copy.OwnerShowId = null;
        copy.OwnerFunctionId = null;
        copy.ShowInFunctionList = true;
        copy.Name = string.IsNullOrWhiteSpace(name) ? Project.SuggestFunctionName(source.Name) : name.Trim();
        Project.Functions.AddRange(copies);
        RefreshPicker();
        RaiseSaved();
        return copy;
    }

    public override void Detach()
    {
        CloseStepEditor(reload: false);
        base.Detach();
    }

    /// <summary>
    /// Also stale when Undo / Redo replaced, removed or added one of the chaser's own step functions (the draft holds
    /// copies of them) or the function of the step editor shown inside this one.
    /// </summary>
    public override bool IsStale(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (base.IsStale(project) || (_stepEditor is { } inner && inner.IsStale(project)))
        {
            return true;
        }

        var current = project.OwnedFunctions(Original.Id);
        if (current.Count != _ownedSources.Count)
        {
            return true;
        }

        foreach (var source in _ownedSources)
        {
            if (!ReferenceEquals(project.FindFunction(source.Id), source))
            {
                return true;
            }
        }

        return false;
    }

    public override void RefreshPatch()
    {
        StepLook?.RefreshPatch();
        foreach (var row in Steps)
        {
            UpdateRow(row);
        }

        _stepEditor?.RefreshPatch();
    }

    protected override void LoadDraft()
    {
        CloseStepEditor(reload: false);
        _owned.Clear();
        _changedOwned.Clear();
        RememberOwnedSources();
        foreach (var function in _ownedSources)
        {
            _owned.Add(DraftCopy(function));
        }

        RunOrder = Chaser.RunOrder;
        Direction = Chaser.Direction;
        PickerSearch = string.Empty;
        IsPickerOpen = false;
        UndoText = null;
        _removed = null;
        StepNote = null;
        ClearPendingCopy();

        // The timing boxes start with the first step's times, so new steps match the existing ones.
        LoadTimingBoxes(Chaser.Steps.FirstOrDefault());

        RunBulk(() =>
        {
            Steps.Clear();
            foreach (var step in Chaser.Steps)
            {
                Steps.Add(CreateRow(step.Clone()));
            }
        });
        _current = null;
        OnStepsChanged(markDirty: false);
        RefreshPicker();
    }

    /// <summary>
    /// Writes the draft. On Save: the steps and playback go into the chaser and the new or changed step functions are
    /// committed with it in one go. For the live preview of the selected step the copy gets only that step, held
    /// without fades and looping, so the lights show it steadily.
    /// </summary>
    protected override void ApplyDraft()
    {
        var target = Chaser;
        if (!ReferenceEquals(target, Original))
        {
            if (!PreviewWholeChaser && _current is { } row)
            {
                target.Steps =
                [
                    new ChaserStep { FunctionId = row.Step.FunctionId, HoldSeconds = FunctionEditing.MaxSeconds },
                ];
                target.RunOrder = RunOrder.Loop;
                target.Direction = RunDirection.Forward;
                return;
            }

            target.Steps = Steps.Select(s => s.Step.Clone()).ToList();
            target.RunOrder = RunOrder;
            target.Direction = Direction;
            return;
        }

        // Saved through this editor's prompt while a step's own editor is shown: save that one.
        if (_stepEditor is { IsDirty: true } inner)
        {
            _savingInner = true;
            try
            {
                inner.Save();
            }
            finally
            {
                _savingInner = false;
            }
        }

        target.Steps = Steps.Select(s => s.Step.Clone()).ToList();
        target.RunOrder = RunOrder;
        target.Direction = Direction;
        var changed = _owned.Where(f => _changedOwned.Contains(f.Id)).ToList();
        Project.CommitChaser(target, changed);
        _changedOwned.Clear();

        // The project stores its own copies: keep the draft copies (same content), drop the ones the commit deleted.
        RememberOwnedSources();
        var stillOwned = _ownedSources.Select(f => f.Id).ToHashSet();
        _owned.RemoveAll(f => !stillOwned.Contains(f.Id));
        UndoText = null;
        _removed = null;
        OnPropertyChanged(nameof(BackToChaserText));
    }

    partial void OnRunOrderChanged(RunOrder value) => OnPlaybackChanged();

    partial void OnDirectionChanged(RunDirection value) => OnPlaybackChanged();

    partial void OnPickerSearchChanged(string value) => RefreshPicker();

    partial void OnPreviewWholeChaserChanged(bool value) => NotifyDraftChanged();

    partial void OnIsPickerOpenChanged(bool value)
    {
        if (value)
        {
            // Every opening starts with a fresh, complete list (functions may have been added meanwhile).
            PickerSearch = string.Empty;
            RefreshPicker();
        }
    }

    // ───────────────────────────── Adding steps ─────────────────────────────

    /// <summary>
    /// Adds an own-look step after the selection (or at the end) that copies the selected step's look, so a pattern is
    /// built by changing one thing per step. When the selected step has no look (a function step), the new step starts
    /// blank with the lights of the nearest look above it.
    /// </summary>
    [RelayCommand]
    private void AddStep()
    {
        var index = ChaserStepListEditing.InsertIndex(Steps.Count, SelectedIndexes);
        var source = _current ?? Steps.LastOrDefault();
        var scene = source?.Function is SceneFunction look
            ? ChaserSteps.NewOwnLookScene(Original.Id, NextStepName(), look.Values)
            : BlankLook(index);
        InsertOwnedStep(index, [scene]);
    }

    /// <summary>Adds an own-look step with the lights of the nearest look above it, at their profile defaults.</summary>
    [RelayCommand]
    private void AddBlankStep()
    {
        var index = ChaserStepListEditing.InsertIndex(Steps.Count, SelectedIndexes);
        InsertOwnedStep(index, [BlankLook(index)]);
    }

    [RelayCommand]
    private void OpenPicker() => IsPickerOpen = true;

    /// <summary>
    /// "Add from function…": adds the picked functions (a single <see cref="ChaserPickerItem"/> or a list of them) as
    /// steps after the last selected step (or at the end). Each step gets the chaser's own copy: a scene becomes an own
    /// look, anything else a function step. The originals are never changed. The new steps become the selection.
    /// </summary>
    [RelayCommand]
    private void AddSteps(object? parameter)
    {
        List<ChaserPickerItem> picked = parameter switch
        {
            ChaserPickerItem item => [item],
            IList list => list.OfType<ChaserPickerItem>().ToList(),
            _ => [],
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

                var copies = Project.CopyForStep(Original.Id, item.Function);
                AddOwned(copies);
                var row = CreateRow(NewStep(copies[0].Id));
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
        AfterStructureChanged();
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

    // ───────────────────────────── Changing the list ─────────────────────────────

    /// <summary>Removes every selected step; <see cref="UndoRemoveCommand"/> brings them back until the next change.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveSteps()
    {
        var selected = SelectedIndexes;
        if (selected.Count == 0)
        {
            return;
        }

        var removed = selected.Select(i => (i, Steps[i])).ToList();
        RunBulk(() =>
        {
            var next = ChaserStepListEditing.SelectionAfterRemove(Steps.Count, selected);
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                Steps.RemoveAt(selected[i]);
            }

            foreach (var row in removed)
            {
                row.Item2.IsSelected = false;
            }

            if (next >= 0)
            {
                Steps[next].IsSelected = true;
            }

            _selectionResetPending = true;
        });

        AfterStructureChanged();
        _removed = removed;
        UndoText = removed.Count == 1 ? $"Removed step {removed[0].i + 1}." : $"Removed {removed.Count} steps.";
    }

    /// <summary>Puts the steps removed last back where they were (and selects them).</summary>
    [RelayCommand(CanExecute = nameof(HasUndo))]
    private void UndoRemove()
    {
        if (_removed is not { } removed)
        {
            UndoText = null;
            return;
        }

        RunBulk(() =>
        {
            foreach (var (index, row) in removed.OrderBy(r => r.Index))
            {
                Steps.Insert(Math.Min(index, Steps.Count), row);
            }

            SelectOnly(removed.Select(r => r.Row));
        });
        AfterStructureChanged();
    }

    /// <summary>
    /// Copies the selected steps after the last selected one. Own steps get their own new copies (the two steps never
    /// share a look); linked steps stay linked.
    /// </summary>
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
            var sources = selected.Select(i => Steps[i]).ToList();
            var copies = new List<ChaserStepRowViewModel>();
            foreach (var source in sources)
            {
                // One at a time, so each new own look gets its own free "Step N" name.
                var copy = CreateRow(DuplicateStep(source));
                Steps.Insert(index++, copy);
                copies.Add(copy);
            }

            SelectOnly(copies);
        });
        AfterStructureChanged();
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

    // ───────────────────────────── Function steps ─────────────────────────────

    /// <summary>
    /// Opens the selected function step's own copy in its normal editor, shown inside this one with "← Back to chaser".
    /// A linked step first becomes the chaser's own copy. With unsaved chaser changes the usual Save / Don't save /
    /// Cancel prompt comes first (the copy must be saved with the chaser before it can be edited on its own).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditStep))]
    private async Task EditStepAsync()
    {
        if (_current is not { Function: not null, HasLook: false } row || row.IsMissing)
        {
            return;
        }

        // Ask first: Cancel (or no way to ask) must leave the draft exactly as it was.
        var index = Steps.IndexOf(row);
        var functionId = row.Step.FunctionId;
        if (IsDirty)
        {
            if (Dialogs is null)
            {
                return;
            }

            switch (await Dialogs.AskSaveChangesAsync(Original.Name))
            {
                case SaveChangesChoice.Cancel:
                    return;
                case SaveChangesChoice.Save:
                    Save();
                    break;
                default:
                    LoadFromOriginal();
                    break;
            }
        }

        // Don't save rebuilds the rows: find the step again (gone when it was new and unsaved).
        if (index < 0 || index >= Steps.Count || Steps[index].Step.FunctionId != functionId)
        {
            return;
        }

        row = Steps[index];
        if (row.IsLinked)
        {
            // A linked step becomes the chaser's own copy. The draft is clean here, so saving commits only that copy,
            // which plays the same as the library function; the step's editor then edits the copy in the project.
            MakeOwnCopy(row);
            Save();
            functionId = row.Step.FunctionId;
        }

        var saved = ((ChaserFunction)Original).Steps;
        if (index < 0 || index >= saved.Count || saved[index].FunctionId != functionId
            || Project.FindFunction(functionId) is not { } function || function.OwnerFunctionId != Original.Id)
        {
            // Don't save dropped the step (or its new copy): there is nothing of the chaser's own to open.
            return;
        }

        OpenStepEditor(function, index);
    }

    /// <summary>"← Back to chaser": closes the step's editor (asking first when it has unsaved changes).</summary>
    [RelayCommand]
    private async Task BackToChaserAsync()
    {
        if (_stepEditor is not { } inner)
        {
            return;
        }

        if (inner.IsDirty)
        {
            if (Dialogs is null)
            {
                return;
            }

            switch (await Dialogs.AskSaveChangesAsync(inner.Original.Name))
            {
                case SaveChangesChoice.Cancel:
                    return;
                case SaveChangesChoice.Save:
                    inner.Save();
                    break;
            }
        }

        CloseStepEditor(reload: true);
    }

    /// <summary>Asks for a name, then <see cref="SaveStepAsFunction"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveStepAsFunction))]
    private void AskSaveStepAsFunction()
    {
        if (_current is not { Function: { } function } row || Dialogs is null)
        {
            return;
        }

        var dialog = new SaveFunctionDialogViewModel(
            "Save step as function",
            $"Adds a copy of step {row.Number} to the Functions list. The step keeps its own copy; changing one never changes the other.",
            Project.SuggestFunctionName(function.Name),
            showFades: false,
            Project.IsFunctionNameTaken);
        if (Dialogs.ShowSaveFunction(dialog))
        {
            SaveStepAsFunction(dialog.TrimmedName);
        }
    }

    private bool CanEditStep => _current is { Function: not null, HasLook: false };

    private bool CanSaveStepAsFunction => _current is { Function: not null };

    private bool CanMoveStepsUp => ChaserStepListEditing.MoveBy(Steps.Count, SelectedIndexes, -1) is not null;

    private bool CanMoveStepsDown => ChaserStepListEditing.MoveBy(Steps.Count, SelectedIndexes, 1) is not null;

    // ───────────────────────────── Draft step functions ─────────────────────────────

    private static LightFunction DraftCopy(LightFunction function)
    {
        var copy = function.CloneAsNew();
        copy.Id = function.Id;
        copy.CopiedFromId = function.CopiedFromId;
        return copy;
    }

    private LightFunction? FindStepFunction(Guid id) => _owned.Find(f => f.Id == id) ?? Project.FindFunction(id);

    private ChaserStepKind KindOf(LightFunction? function) => function switch
    {
        null => ChaserStepKind.Missing,
        { OwnerFunctionId: var owner } when owner == Original.Id => function is SceneFunction ? ChaserStepKind.OwnLook : ChaserStepKind.FunctionStep,
        _ => ChaserStepKind.Linked,
    };

    private void AddOwned(IEnumerable<LightFunction> functions)
    {
        foreach (var function in functions)
        {
            _owned.Add(function);
            _changedOwned.Add(function.Id);
        }
    }

    /// <summary>"Step 5": the first free "Step N" name among the steps, counting from the number of steps + 1.</summary>
    private string NextStepName()
    {
        var names = Steps.Select(s => s.FunctionName).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var number = Steps.Count + 1;
        while (names.Contains($"Step {number}"))
        {
            number++;
        }

        return $"Step {number}";
    }

    /// <summary>A new own look with the lights of the nearest look above <paramref name="index"/>, at profile defaults.</summary>
    private SceneFunction BlankLook(int index)
    {
        var scene = ChaserSteps.NewOwnLookScene(Original.Id, NextStepName(), []);
        foreach (var id in NearestLookLights(index))
        {
            if (Project.FindFixture(id) is { } fixture)
            {
                scene.AddFixture(Project, fixture);
            }
        }

        return scene;
    }

    private IReadOnlyList<Guid> NearestLookLights(int index)
    {
        for (var i = Math.Min(index, Steps.Count) - 1; i >= 0; i--)
        {
            if (Steps[i].Function is SceneFunction scene)
            {
                return scene.TargetFixtureIds();
            }
        }

        return [];
    }

    private ChaserStep NewStep(Guid functionId) => new()
    {
        FunctionId = functionId,
        HoldSeconds = _newHoldSeconds,
        FadeInSeconds = _newFadeInSeconds,
        FadeOutSeconds = _newFadeOutSeconds,
    };

    private void InsertOwnedStep(int index, IReadOnlyList<LightFunction> functions)
    {
        AddOwned(functions);
        RunBulk(() =>
        {
            var row = CreateRow(NewStep(functions[0].Id));
            Steps.Insert(Math.Min(index, Steps.Count), row);
            SelectOnly([row]);
        });
        AfterStructureChanged();
    }

    private ChaserStep DuplicateStep(ChaserStepRowViewModel row)
    {
        var step = row.Step.Clone();
        if (row.Function is { } function && (row.IsOwnLook || row.IsFunctionStep))
        {
            var ownedIds = _owned.Select(f => f.Id).ToHashSet();
            var pool = _owned.Concat(Project.Functions.Where(f => !ownedIds.Contains(f.Id))).ToList();
            var copies = ChaserSteps.CloneWithOwned(function, pool);
            copies[0].OwnerFunctionId = Original.Id;
            if (row.IsOwnLook)
            {
                copies[0].Name = NextStepName();
            }

            AddOwned(copies);
            step.FunctionId = copies[0].Id;
        }

        return step;
    }

    /// <summary>A linked step becomes the chaser's own copy (the library function is never changed).</summary>
    private void MakeOwnCopy(ChaserStepRowViewModel row)
    {
        if (row.Function is not { } library || !row.IsLinked)
        {
            return;
        }

        var copies = ReferenceEquals(row, _pendingCopyRow) && _pendingCopies is { } pending
            ? pending
            : Project.CopyForStep(Original.Id, library);
        ClearPendingCopy();
        AdoptCopy(row, copies, library.Name);
    }

    private void AdoptCopy(ChaserStepRowViewModel row, IReadOnlyList<LightFunction> copies, string originalName)
    {
        AddOwned(copies);
        row.Step.FunctionId = copies[0].Id;
        UpdateRow(row);
        StepNote = $"Step {row.Number} is now this chaser's own copy; the original \"{originalName}\" is unchanged.";
        OnCurrentStepChanged();
        MarkDirty();
    }

    private void ClearPendingCopy()
    {
        _pendingCopyRow = null;
        _pendingCopies = null;
    }

    // ───────────────────────────── Rows, selection, panel ─────────────────────────────

    private ChaserStepRowViewModel CreateRow(ChaserStep step)
    {
        var function = FindStepFunction(step.FunctionId);
        return new ChaserStepRowViewModel(step, function, KindOf(function), Steps.Count + 1, ColorOf(function), OnRowTimingChanged, OnRowSelectionChanged);
    }

    private string? ColorOf(LightFunction? function) => ShowsLibraryItem.PreviewColor(function, Project)?.ToHex();

    private void UpdateRow(ChaserStepRowViewModel row)
    {
        var function = FindStepFunction(row.Step.FunctionId);
        row.Update(function, KindOf(function), ColorOf(function));
    }

    private void SelectOnly(IEnumerable<ChaserStepRowViewModel> rows)
    {
        var keep = rows.ToHashSet();
        foreach (var row in Steps)
        {
            row.IsSelected = keep.Contains(row);
        }

        _current = keep.Count > 0 ? Steps.FirstOrDefault(keep.Contains) : null;
        _selectionResetPending = true;
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
        AfterStructureChanged();
    }

    /// <summary>Add / duplicate / move / remove / undo: the old Undo no longer fits, refresh and count as an edit.</summary>
    private void AfterStructureChanged()
    {
        UndoText = null;
        _removed = null;
        OnStepsChanged(markDirty: true);
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

        _current = _current is { IsSelected: true } && Steps.Contains(_current) ? _current : Steps.FirstOrDefault(s => s.IsSelected);
        if (!ReferenceEquals(_current, _shownRow) || (_current is not null && !IsShowing(_current)))
        {
            ShowCurrentStep();
        }
        else
        {
            OnCurrentStepChanged();
        }

        if (Steps.FirstOrDefault(s => s.IsSelected) is { } first)
        {
            LoadTimingBoxes(first.Step);
        }

        if (_selectionResetPending)
        {
            _selectionResetPending = false;
            SelectionReset?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsShowing(ChaserStepRowViewModel row) => !row.HasLook
        || (StepLook is { } look && (ReferenceEquals(look.Scene, row.Function) || (ReferenceEquals(row, _pendingCopyRow) && _pendingCopies is { } p && ReferenceEquals(look.Scene, p[0]))));

    /// <summary>Points the right panel at <see cref="_current"/> and refreshes the preview of the selected step.</summary>
    private void ShowCurrentStep()
    {
        _shownRow = _current;
        ClearPendingCopy();
        StepNote = null;
        if (_current is { HasLook: true, Function: SceneFunction scene } row)
        {
            if (row.IsLinked)
            {
                // The panel edits the chaser's own copy; it replaces the library scene on the first change.
                var copies = Project.CopyForStep(Original.Id, scene);
                _pendingCopyRow = row;
                _pendingCopies = copies;
                scene = (SceneFunction)copies[0];
            }

            ShowLook(scene);
        }

        OnCurrentStepChanged();
        if (!PreviewWholeChaser)
        {
            NotifyDraftChanged();
        }
    }

    private void ShowLook(SceneFunction scene)
    {
        _ignoreLookChanges = true;
        try
        {
            if (StepLook is null)
            {
                StepLook = new ChaserStepLookViewModel(Project, scene, _settings, _clipboard, defaultLightIds: null);
                StepLook.Changed += OnLookChanged;
                OnPropertyChanged(nameof(StepLook));
            }
            else
            {
                StepLook.SetScene(scene);
            }
        }
        finally
        {
            _ignoreLookChanges = false;
        }
    }

    private void OnCurrentStepChanged()
    {
        if (StepLook is not null && _current is { } row)
        {
            StepLook.Title = $"Step {row.Number} · {row.FunctionName}";
        }

        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsLookPanelVisible));
        OnPropertyChanged(nameof(IsFunctionPanelVisible));
        OnPropertyChanged(nameof(IsNoStepPanelVisible));
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(LinkedHint));
        OnPropertyChanged(nameof(HasLinkedHint));
        EditStepCommand.NotifyCanExecuteChanged();
        AskSaveStepAsFunctionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The look panel changed the shown step's scene (always a draft copy).</summary>
    private void OnLookChanged(object? sender, EventArgs e)
    {
        if (_ignoreLookChanges || _current is not { } row || StepLook is null)
        {
            return;
        }

        if (ReferenceEquals(row, _pendingCopyRow) && _pendingCopies is { } copies && row.Function is { } library)
        {
            ClearPendingCopy();
            AdoptCopy(row, copies, library.Name);
        }

        _changedOwned.Add(StepLook.Scene.Id);
        UpdateRow(row);
        MarkDirty();
    }

    private void OnRowSelectionChanged(ChaserStepRowViewModel row)
    {
        if (_bulkUpdate)
        {
            return;
        }

        if (row.IsSelected && _current is not { IsSelected: true })
        {
            _current = row;
        }

        OnSelectionChanged();
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

    private void LoadTimingBoxes(ChaserStep? step)
    {
        _newHoldSeconds = step?.HoldSeconds ?? ChaserStep.DefaultHoldSeconds;
        _newFadeInSeconds = step?.FadeInSeconds ?? 0;
        _newFadeOutSeconds = step?.FadeOutSeconds ?? 0;
        OnPropertyChanged(nameof(NewHoldSeconds));
        OnPropertyChanged(nameof(NewFadeInSeconds));
        OnPropertyChanged(nameof(NewFadeOutSeconds));
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
        var candidates = 0;
        foreach (var function in Project.FunctionListItems.OrderBy(f => f.Type).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!Project.CanPlayInChaser(Original.Id, function))
            {
                continue;
            }

            candidates++;
            if (text.Length == 0
                || function.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || FunctionRowViewModel.TypeDisplayName(function.Type).Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                PickerItems.Add(new ChaserPickerItem(function, ShowsLibraryItem.PreviewColor(function, Project)?.ToHex()));
            }
        }

        HasNoCandidates = candidates == 0;
        HasNoMatches = candidates > 0 && PickerItems.Count == 0;
        OnPropertyChanged(nameof(HasNoCandidates));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(NoMatchesText));
    }

    // ───────────────────────────── Step editor inside the chaser ─────────────────────────────

    private void OpenStepEditor(LightFunction function, int index)
    {
        var inner = FunctionEditorFactory.Create(Project, function, _settings, _clipboard);
        if (inner is ChaserEditorViewModel innerChaser)
        {
            innerChaser.Dialogs = Dialogs;
        }

        inner.PreviewStatus = PreviewStatus;
        inner.DraftChanged += OnInnerDraftChanged;
        inner.Saved += OnInnerSaved;
        inner.CloseRequested += OnInnerCloseRequested;
        inner.PropertyChanged += OnInnerPropertyChanged;
        _stepEditor = inner;
        _stepEditorIndex = index;
        OnStepEditorChanged();
    }

    private void CloseStepEditor(bool reload)
    {
        if (_stepEditor is not { } inner)
        {
            return;
        }

        inner.DraftChanged -= OnInnerDraftChanged;
        inner.Saved -= OnInnerSaved;
        inner.CloseRequested -= OnInnerCloseRequested;
        inner.PropertyChanged -= OnInnerPropertyChanged;
        inner.Detach();
        _stepEditor = null;
        var index = _stepEditorIndex;
        _stepEditorIndex = -1;
        OnStepEditorChanged();
        if (!reload)
        {
            return;
        }

        // The chaser was saved before the step's editor opened: re-read it (the copy may have changed), same step selected.
        LoadFromOriginal();
        if (index >= 0 && index < Steps.Count)
        {
            RunBulk(() => SelectOnly([Steps[index]]));
            OnSelectionChanged();
        }
    }

    private void OnStepEditorChanged()
    {
        OnPropertyChanged(nameof(StepEditor));
        OnPropertyChanged(nameof(InnerEditor));
        OnPropertyChanged(nameof(IsInnerEditorOpen));
        OnPropertyChanged(nameof(BackToChaserText));

        // While the step's editor is shown the chaser has no own changes; its "unsaved" state is the inner editor's.
        IsDirty = _stepEditor?.IsDirty ?? false;
        NotifyDraftChanged();
    }

    private void OnInnerDraftChanged(object? sender, EventArgs e) => NotifyDraftChanged();

    private void OnInnerSaved(object? sender, EventArgs e)
    {
        if (sender is FunctionEditorViewModel inner)
        {
            var index = _owned.FindIndex(f => f.Id == inner.Original.Id);
            if (index >= 0)
            {
                _owned[index] = DraftCopy(inner.Original);
            }
        }

        // A step chaser's save may replace its own step functions in the project (they are ours too, recursively).
        RememberOwnedSources();

        // While this chaser's own Save saves the step editor, that Save raises Saved once for both.
        if (!_savingInner)
        {
            RaiseSaved();
        }
    }

    private void RememberOwnedSources()
    {
        _ownedSources.Clear();
        _ownedSources.AddRange(Project.OwnedFunctions(Original.Id));
    }

    private void OnInnerCloseRequested(object? sender, EventArgs e) => _ = BackToChaserAsync();

    private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsDirty) && sender is FunctionEditorViewModel inner && ReferenceEquals(inner, _stepEditor))
        {
            IsDirty = inner.IsDirty;
        }
    }

    private void OnOwnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The preview service reports to this (attached) editor; the step's editor shows the same status.
        if (e.PropertyName == nameof(PreviewStatus) && _stepEditor is { } inner)
        {
            inner.PreviewStatus = PreviewStatus;
        }
    }
}
