using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Channel layout editor (QLC+ "fixture editor", simplified): the user says what each DMX channel of their light does,
/// in the light's own order. Saves a custom, unverified profile to the user library; editing a layout the project uses
/// updates every light that uses it. Built-in profiles are only templates and are never overwritten.
/// </summary>
public sealed partial class ChannelLayoutViewModel : ObservableObject
{
    private readonly ProfileLibraryService _library;
    private readonly ProjectService _projects;
    private readonly IDialogService _dialogs;

    /// <summary>The custom layout being edited in place; null when a new layout is created.</summary>
    private readonly FixtureProfile? _existing;

    /// <summary>True while the channel count is changed from code, so it does not resize the rows again.</summary>
    private bool _syncingCount;
    private bool _loading;
    private bool _closeConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _manufacturer = ChannelLayout.DefaultManufacturer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyTemplateCommand))]
    private ChannelLayoutTemplate? _selectedTemplate;

    [ObservableProperty]
    private int _channelCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(DeleteRowCommand),
        nameof(MoveRowUpCommand),
        nameof(MoveRowDownCommand),
        nameof(DuplicateRowCommand))]
    private ChannelLayoutRowViewModel? _selectedRow;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    private string? _saveError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    private ChannelLayoutApplyTarget? _selectedApplyTarget;

    /// <param name="library">User profile library the layout is saved to.</param>
    /// <param name="projects">Open project; a layout it uses is updated in place on save.</param>
    /// <param name="dialogs">Confirmations and messages.</param>
    /// <param name="source">Profile to start from (null = new layout from the first template).</param>
    /// <param name="modeName">Mode of <paramref name="source"/> to copy (null = its largest mode).</param>
    /// <param name="editSource">
    /// True to edit <paramref name="source"/> itself (custom layouts only); false to save a new layout.
    /// </param>
    public ChannelLayoutViewModel(
        ProfileLibraryService library,
        ProjectService projects,
        IDialogService dialogs,
        FixtureProfile? source = null,
        string? modeName = null,
        bool editSource = false)
    {
        _library = library;
        _projects = projects;
        _dialogs = dialogs;
        _existing = editSource && source is not null && ChannelLayout.IsCustom(source) ? source : null;

        Templates = ChannelLayout.Templates.Select(t => new ChannelLayoutTemplate(t.Name, t.Rows)).ToList();
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRows));

        _loading = true;
        try
        {
            if (source is null)
            {
                SelectedTemplate = Templates.FirstOrDefault();
                LoadRows(SelectedTemplate?.Rows ?? [new ChannelLayoutRow()]);
            }
            else
            {
                var mode = source.FindMode(modeName) ?? source.Modes.MaxBy(m => m.ChannelCount);
                LoadRows(mode is null ? [new ChannelLayoutRow()] : ChannelLayout.FromMode(source, mode.Name));
                Model = source.Model;
                Manufacturer = _existing is not null && !string.IsNullOrWhiteSpace(source.Manufacturer)
                    ? source.Manufacturer
                    : ChannelLayout.DefaultManufacturer;
                SourceName = source.DisplayName;
            }
        }
        finally
        {
            _loading = false;
        }

        UsedByCount = _existing is null
            ? 0
            : _projects.Current.Fixtures.Count(f => string.Equals(f.ProfileId, _existing.Id, StringComparison.OrdinalIgnoreCase));
        SelectedRow = Rows.FirstOrDefault();
        Revalidate();
        IsDirty = false;
    }

    /// <summary>Raised when the window should close; the argument is true after a successful save.</summary>
    public event EventHandler<bool>? CloseRequested;

    public ObservableCollection<ChannelLayoutRowViewModel> Rows { get; } = [];

    public IReadOnlyList<ChannelLayoutTemplate> Templates { get; }

    public static IReadOnlyList<ChannelLayoutTypeOption> TypeOptions => ChannelLayoutTypeOption.All;

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public ObservableCollection<ChannelLayoutApplyTarget> ApplyTargets { get; } = [];

    public bool IsEditingExisting => _existing is not null;

    /// <summary>Display name of the profile the rows were copied from (null for a blank new layout).</summary>
    public string? SourceName { get; }

    /// <summary>How many patched lights use the layout being edited (they all change on save).</summary>
    public int UsedByCount { get; }

    public bool HasUsedBy => UsedByCount > 0;

    public string UsedByText => UsedByCount == 1
        ? "Used by 1 light: saving updates it."
        : $"Used by {UsedByCount} lights: saving updates all of them.";

    /// <summary>One-line note about where the rows came from (shown for copies of a built-in profile).</summary>
    public string? SourceHint => _existing is null && SourceName is not null
        ? $"Copied from \"{SourceName}\". The original stays unchanged; this saves a new layout."
        : null;

    public bool HasSourceHint => SourceHint is not null;

    public string WindowTitle => _existing is not null
        ? $"Edit channel layout · {_existing.DisplayName}"
        : "New channel layout";

    public bool HasRows => Rows.Count > 0;

    public bool HasIssues => Issues.Count > 0;

    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);

    public bool HasSaveError => !string.IsNullOrEmpty(SaveError);

    public bool HasApplyTargets => ApplyTargets.Count > 0;

    /// <summary>The saved profile after a successful <see cref="SaveCommand"/>.</summary>
    public FixtureProfile? SavedProfile { get; private set; }

    /// <summary>Lights to switch to the new layout (from the "Use it for" choice); empty when none.</summary>
    public IReadOnlyList<Guid> FixturesToSwitch => SelectedApplyTarget?.FixtureIds ?? [];

    /// <summary>Offers the "Use it for" choice; the first target is preselected.</summary>
    public void SetApplyTargets(IEnumerable<ChannelLayoutApplyTarget> targets)
    {
        ApplyTargets.Clear();
        foreach (var target in targets)
        {
            ApplyTargets.Add(target);
        }

        SelectedApplyTarget = ApplyTargets.FirstOrDefault();
        OnPropertyChanged(nameof(HasApplyTargets));
    }

    /// <summary>Called by the window before it closes: asks before throwing away unsaved edits.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (_closeConfirmed || !IsDirty)
        {
            return true;
        }

        _closeConfirmed = await _dialogs.ConfirmAsync(
            "Unsaved channel layout",
            "Close the editor and discard your changes to this channel layout?",
            "Discard");
        return _closeConfirmed;
    }

    partial void OnModelChanged(string value) => OnEdited();

    partial void OnManufacturerChanged(string value) => OnEdited();

    partial void OnSelectedRowChanged(ChannelLayoutRowViewModel? oldValue, ChannelLayoutRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnChannelCountChanged(int value)
    {
        if (!_syncingCount && !_loading)
        {
            _ = ResizeAsync(value);
        }
    }

    // ───────────────────────────── Commands ─────────────────────────────

    /// <summary>Replaces all rows with the chosen template (asks first when that would lose work).</summary>
    [RelayCommand(CanExecute = nameof(HasTemplate))]
    private async Task ApplyTemplateAsync()
    {
        var template = SelectedTemplate!;
        if (Rows.Any(r => r.IsMeaningful) &&
            !await _dialogs.ConfirmAsync(
                "Replace channels",
                $"Replace all {Rows.Count} channels with the \"{template.Name}\" template?",
                "Replace"))
        {
            return;
        }

        LoadRows(template.Rows);
        SelectedRow = Rows.FirstOrDefault();
        OnStructureChanged();
    }

    /// <summary>Inserts an Unknown channel above the selected one (or at the end).</summary>
    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void InsertRow()
    {
        var index = SelectedRow is null ? Rows.Count : Rows.IndexOf(SelectedRow);
        var row = CreateRow(new ChannelLayoutRow());
        Rows.Insert(index, row);
        SelectedRow = row;
        OnStructureChanged();
    }

    /// <summary>Adds an Unknown channel at the end.</summary>
    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddRow()
    {
        var row = CreateRow(new ChannelLayoutRow());
        Rows.Add(row);
        SelectedRow = row;
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRow))]
    private void DeleteRow()
    {
        var index = Rows.IndexOf(SelectedRow!);
        Rows.RemoveAt(index);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDuplicateRow))]
    private void DuplicateRow()
    {
        var index = Rows.IndexOf(SelectedRow!);
        var copy = SelectedRow!.Clone();
        Rows.Insert(index + 1, copy);
        SelectedRow = copy;
        OnStructureChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveRowUp))]
    private void MoveRowUp() => MoveRow(-1);

    [RelayCommand(CanExecute = nameof(CanMoveRowDown))]
    private void MoveRowDown() => MoveRow(1);

    /// <summary>Saves the layout to the user library and, when the project uses it, updates those lights.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        Revalidate();
        if (HasErrors)
        {
            SaveError = "Fix the errors in the list before saving.";
            return;
        }

        IsSaving = true;
        try
        {
            SaveError = null;
            var manufacturer = string.IsNullOrWhiteSpace(Manufacturer) ? ChannelLayout.DefaultManufacturer : Manufacturer.Trim();
            var profile = ChannelLayout.CreateProfile(manufacturer, Model.Trim(), Rows.Select(r => r.ToRow()).ToList(), _existing);

            var saveIssues = await _library.SaveUserProfileAsync(profile);
            var errors = saveIssues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message).ToList();
            if (errors.Count > 0)
            {
                SaveError = errors.Count == 1 ? errors[0] : $"{errors[0]} (+{errors.Count - 1} more problems)";
                return;
            }

            var project = _projects.Current;
            if (_existing is not null && project.FindProfile(profile.Id) is not null)
            {
                var result = project.UpdateProfile(profile);
                _projects.MarkChanged();
                var problems = result.PatchIssues.Concat(result.FunctionIssues).ToList();
                if (problems.Count > 0)
                {
                    const int MaxShown = 8;
                    var lines = string.Join(Environment.NewLine, problems.Take(MaxShown).Select(i => "• " + i.Message));
                    var more = problems.Count > MaxShown ? $"{Environment.NewLine}… and {problems.Count - MaxShown} more." : string.Empty;
                    var lights = result.AffectedFixtureIds.Count == 1 ? "1 light" : $"{result.AffectedFixtureIds.Count} lights";
                    await _dialogs.ShowInfoAsync(
                        "Layout saved: check these problems",
                        $"The layout was saved and {lights} updated. The new layout causes these problems:" +
                        $"{Environment.NewLine}{lines}{more}{Environment.NewLine}{Environment.NewLine}" +
                        "Fix overlaps on the Fixtures page (e.g. Auto-patch) and check scenes that used removed channels.");
                }
            }

            SavedProfile = profile;
            IsDirty = false;
            _closeConfirmed = true;
            CloseRequested?.Invoke(this, true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private bool HasTemplate() => SelectedTemplate is not null;

    private bool CanAddRow() => Rows.Count < ChannelLayout.MaxChannels;

    private bool CanDeleteRow() => SelectedRow is not null && Rows.Count > 1;

    private bool CanDuplicateRow() => SelectedRow is not null && Rows.Count < ChannelLayout.MaxChannels;

    private bool CanMoveRowUp() => SelectedRow is not null && Rows.IndexOf(SelectedRow) > 0;

    private bool CanMoveRowDown() => SelectedRow is not null && Rows.IndexOf(SelectedRow) < Rows.Count - 1;

    private bool CanSave() => !IsSaving && !HasErrors;

    // ───────────────────────────── Helpers ─────────────────────────────

    private void MoveRow(int delta)
    {
        var row = SelectedRow!;
        var index = Rows.IndexOf(row);
        Rows.Move(index, index + delta);
        SelectedRow = row;
        OnStructureChanged();
    }

    /// <summary>Grows with Unknown channels at the end, or removes channels from the end (asking when work is lost).</summary>
    private async Task ResizeAsync(int requested)
    {
        var target = Math.Clamp(requested, 1, ChannelLayout.MaxChannels);
        if (target < Rows.Count)
        {
            var lost = Rows.Skip(target).Count(r => r.IsMeaningful);
            var range = target + 1 == Rows.Count ? $"channel {Rows.Count}" : $"channels {target + 1}–{Rows.Count}";
            if (lost > 0 &&
                !await _dialogs.ConfirmAsync(
                    "Remove channels",
                    $"Remove {range}? {lost} of them have a function or a name.",
                    "Remove"))
            {
                SetChannelCountSilently(Rows.Count);
                return;
            }

            while (Rows.Count > target)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }

            if (SelectedRow is not null && !Rows.Contains(SelectedRow))
            {
                SelectedRow = Rows.LastOrDefault();
            }
        }
        else
        {
            while (Rows.Count < target)
            {
                Rows.Add(CreateRow(new ChannelLayoutRow()));
            }
        }

        OnStructureChanged();
    }

    private void LoadRows(IEnumerable<ChannelLayoutRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows.Take(ChannelLayout.MaxChannels))
        {
            Rows.Add(CreateRow(row.Clone()));
        }

        RefreshNumbers();

        // A name equal to the default name is shown blank, so it follows later function changes.
        foreach (var row in Rows.Where(r => r.Name == r.Placeholder).ToList())
        {
            row.Name = string.Empty;
        }

        SetChannelCountSilently(Rows.Count);
    }

    private ChannelLayoutRowViewModel CreateRow(ChannelLayoutRow row) => new(row, OnRowChanged);

    private void OnRowChanged(ChannelLayoutRowViewModel row)
    {
        if (_loading)
        {
            return;
        }

        RefreshNumbers();
        OnEdited();
    }

    private void OnStructureChanged()
    {
        RefreshNumbers();
        SetChannelCountSilently(Rows.Count);
        NotifyRowCommands();
        OnEdited();
    }

    private void OnEdited()
    {
        if (_loading)
        {
            return;
        }

        IsDirty = true;
        SaveError = null;
        Revalidate();
    }

    /// <summary>
    /// Renumbers channels 1..n and recomputes the default names shown as placeholders, the same way
    /// <see cref="ChannelLayout.CreateProfile"/> names blank channels ("Red 2", "Dimmer fine", "Channel 5").
    /// </summary>
    private void RefreshNumbers()
    {
        var seen = new Dictionary<ChannelType, int>();
        string? lastCoarse = null;
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            row.Number = i + 1;
            var occurrence = seen.GetValueOrDefault(row.Type) + 1;
            seen[row.Type] = occurrence;
            row.Placeholder = row.Type switch
            {
                ChannelType.Generic => ChannelLayout.DefaultNameFor(ChannelType.Generic, row.Number),
                ChannelType.Fine when lastCoarse is not null => $"{lastCoarse} fine",
                _ => ChannelLayout.DefaultNameFor(row.Type, occurrence),
            };

            if (row.Type != ChannelType.Fine)
            {
                lastCoarse = row.DisplayName;
            }
        }
    }

    private void Revalidate()
    {
        Issues.Clear();
        foreach (var issue in ChannelLayout.Validate(Model, Rows.Select(r => r.ToRow()).ToList()).OrderByDescending(i => i.Severity))
        {
            Issues.Add(issue);
        }

        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasErrors));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SetChannelCountSilently(int count)
    {
        _syncingCount = true;
        try
        {
            ChannelCount = count;
        }
        finally
        {
            _syncingCount = false;
        }
    }

    private void NotifyRowCommands()
    {
        InsertRowCommand.NotifyCanExecuteChanged();
        AddRowCommand.NotifyCanExecuteChanged();
        DeleteRowCommand.NotifyCanExecuteChanged();
        DuplicateRowCommand.NotifyCanExecuteChanged();
        MoveRowUpCommand.NotifyCanExecuteChanged();
        MoveRowDownCommand.NotifyCanExecuteChanged();
    }
}
