using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.Validation;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Fixtures page (QLC+ style): fixture table + address map on the left, Properties and the Groups tree on the right.
/// Table selection is tracked per row (<see cref="FixtureRowViewModel.IsSelected"/>) so a group can select its fixtures.
/// </summary>
public sealed partial class FixturesViewModel : PageViewModel
{
    /// <summary>Patch problems shown under a specific Properties field instead of in the general issue list.</summary>
    private static readonly HashSet<string> AddressCodes = ["ADDRESS_OUT_OF_RANGE", "FOOTPRINT_EXCEEDS_UNIVERSE", "ADDRESS_OVERLAP"];
    private const string UniverseCode = "UNIVERSE_INVALID";
    private const int MapColumns = 32;

    private static readonly UniverseFilterOption AllUniverses = new(null, "All universes");

    private readonly ProjectService _projects;
    private readonly ProfileLibraryService _library;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _fileDialogs;

    /// <summary>
    /// Rows by fixture (also those hidden by the filter), so a filter change or an edit reuses them. Rows of removed
    /// fixtures are dropped after each content change; the whole cache is dropped when another project is opened.
    /// </summary>
    private readonly Dictionary<Fixture, FixtureRowViewModel> _rowCache = new(ReferenceEqualityComparer.Instance);

    /// <summary>The project the cached rows belong to.</summary>
    private Project? _rowCacheProject;

    private IReadOnlyList<FixtureProfile> _profileOptions = [];

    /// <summary>True while the Groups tree is rebuilt, so restoring its selection doesn't change the table selection.</summary>
    private bool _rebuildingGroups;

    /// <summary>True during <see cref="Reload"/>: list rebuilds push transient values (e.g. a null filter) back.</summary>
    private bool _reloading;

    /// <summary><see cref="ProjectService.Revision"/> the lists were last rebuilt from (-1 = never).</summary>
    private long _loadedRevision = -1;

    /// <summary>The profile library changed since the last rebuild.</summary>
    private bool _libraryChangedSinceLoad;

    /// <summary>Patch issues of the last validation and the project revision they belong to (selection changes reuse them).</summary>
    private IReadOnlyList<ValidationIssue>? _issues;
    private long _issuesRevision = -1;

    /// <summary>A selection-command refresh is already queued for this dispatcher turn.</summary>
    private bool _selectionCommandsPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(AutoAddressCommand), nameof(EditChannelLayoutCommand))]
    private FixtureRowViewModel? _selectedFixture;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private UniverseFilterOption _universeFilter = AllUniverses;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapUniverseSelection))]
    private int _mapUniverse = 1;

    [ObservableProperty]
    private string _mapSummary = string.Empty;

    /// <summary>Selected node of the Groups tree: a <see cref="GroupRowViewModel"/> or a <see cref="GroupMemberViewModel"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedGroup), nameof(SelectedMember))]
    [NotifyCanExecuteChangedFor(
        nameof(AddToGroupCommand),
        nameof(RemoveFromGroupCommand),
        nameof(MoveMemberUpCommand),
        nameof(MoveMemberDownCommand),
        nameof(RenameGroupCommand),
        nameof(DeleteGroupCommand),
        nameof(DeleteGroupNodeCommand))]
    private object? _selectedGroupNode;

    /// <summary>
    /// Raised after the ViewModel changed <see cref="FixtureRowViewModel.IsSelected"/> of rows itself (a group was
    /// picked, the table was rebuilt), so the view can update the table's own selection. The table virtualizes its rows,
    /// so a per-row binding would only reach the rows currently on screen.
    /// </summary>
    public event EventHandler? TableSelectionChanged;

    public FixturesViewModel(
        ProjectService projects,
        ProfileLibraryService library,
        IDialogService dialogs,
        IFileDialogService fileDialogs)
    {
        _projects = projects;
        _library = library;
        _dialogs = dialogs;
        _fileDialogs = fileDialogs;
        for (var a = DmxAddress.Min; a <= DmxAddress.Max; a++)
        {
            AddressCells.Add(new AddressCellViewModel(a));
        }

        _projects.ProjectChanged += (_, _) => Reload();

        // Undo / Redo: fixtures whose content changed are new objects; rebuild the rows now (also when hidden: cheap).
        _projects.ContentRestored += (_, _) => Reload();
        _library.LibraryChanged += (_, _) =>
        {
            _profileOptions = BuildProfileOptions();
            _libraryChangedSinceLoad = true;
        };
        Reload();
    }

    public override string Title => "Fixtures";

    public override string Subtitle => "Patch your lights: profile, mode, universe and start address.";

    public ObservableCollection<FixtureRowViewModel> Fixtures { get; } = [];

    /// <summary>Top-level nodes of the Groups tree.</summary>
    public ObservableCollection<GroupRowViewModel> Groups { get; } = [];

    public ObservableCollection<AddressCellViewModel> AddressCells { get; } = [];

    /// <summary>First address of each row of the 32-column address map (1, 33, 65, …).</summary>
    public IReadOnlyList<int> MapRowLabels { get; } =
        Enumerable.Range(0, DmxAddress.UniverseSize / MapColumns).Select(r => (r * MapColumns) + 1).ToList();

    public ObservableCollection<int> Universes { get; } = [];

    /// <summary>
    /// <see cref="MapUniverse"/> for the map's ComboBox. Nullable because the ComboBox pushes null while its items are
    /// rebuilt; that is ignored instead of failing the binding.
    /// </summary>
    public int? MapUniverseSelection
    {
        get => MapUniverse;
        set
        {
            if (value is { } universe)
            {
                MapUniverse = universe;
            }
        }
    }

    public ObservableCollection<UniverseFilterOption> UniverseFilters { get; } = [];

    /// <summary>Problems of the selected fixture that no Properties field shows itself.</summary>
    public ObservableCollection<ValidationIssue> SelectedIssues { get; } = [];

    public IReadOnlyList<FixtureProfile> ProfileOptions => _profileOptions;

    public bool HasSelection => SelectedFixture is not null;

    /// <summary>No fixtures in the project at all: the table shows the "No fixtures yet" empty state.</summary>
    public bool IsProjectEmpty => _projects.Current.Fixtures.Count == 0;

    /// <summary>Fixtures exist but the search/universe filter hides all of them.</summary>
    public bool NoFilterResults => !IsProjectEmpty && Fixtures.Count == 0;

    public bool HasGroups => Groups.Count > 0;

    public bool HasNoGroups => Groups.Count == 0;

    public GroupRowViewModel? SelectedGroup => SelectedGroupNode switch
    {
        GroupRowViewModel g => g,
        GroupMemberViewModel m => m.Parent,
        _ => null,
    };

    public GroupMemberViewModel? SelectedMember => SelectedGroupNode as GroupMemberViewModel;

    /// <summary>Fixtures selected in the table, in table order (falls back to the focused row).</summary>
    public IReadOnlyList<FixtureRowViewModel> SelectedRows
    {
        get
        {
            var rows = Fixtures.Where(r => r.IsSelected).ToList();
            if (rows.Count == 0 && SelectedFixture is not null)
            {
                rows.Add(SelectedFixture);
            }

            return rows;
        }
    }

    /// <summary>
    /// Rebuilds the table, groups and map only when the project or the profile library changed since the last rebuild:
    /// rebuilding every row (the DataGrid regenerates them all) on each visit made switching to this page slow.
    /// </summary>
    public override void OnNavigatedTo()
    {
        if (_loadedRevision != _projects.Revision || _libraryChangedSinceLoad)
        {
            Reload();
        }
    }

    partial void OnSelectedFixtureChanged(FixtureRowViewModel? value)
    {
        if (value is not null && value.Universe != MapUniverse && Universes.Contains(value.Universe))
        {
            MapUniverse = value.Universe;
        }

        UpdateSelectedIssues();
        UpdateAddressMap();
        NotifySelectionCommands();
    }

    partial void OnSearchChanged(string value) => Reload();

    partial void OnUniverseFilterChanged(UniverseFilterOption value)
    {
        if (_reloading)
        {
            return;
        }

        // The ComboBox pushes null when its selected item disappears from the list.
        if (value is null)
        {
            UniverseFilter = AllUniverses;
            return;
        }

        Reload();
    }

    partial void OnMapUniverseChanged(int value) => UpdateAddressMap();

    // ───────────────────────────── Fixtures ─────────────────────────────

    [RelayCommand]
    private void AddFixture()
    {
        var dialog = new AddFixtureViewModel(_profileOptions, _projects.Current, _library, _fileDialogs, _dialogs, _projects) { Universe = MapUniverse };
        if (!_dialogs.ShowAddFixture(dialog))
        {
            return;
        }

        var added = dialog.Apply();
        _projects.MarkChanged();
        Reload();
        SelectedFixture = Fixtures.FirstOrDefault(r => r.Fixture == added[^1]) ?? SelectedFixture;
    }

    [RelayCommand(CanExecute = nameof(HasRowSelection))]
    private async Task DeleteAsync()
    {
        var rows = SelectedRows;
        if (rows.Count == 0)
        {
            return;
        }

        var what = rows.Count == 1 ? $"\"{rows[0].Name}\"" : $"{rows.Count} fixtures";
        if (!await _dialogs.ConfirmAsync("Delete fixtures", $"Delete {what}? They are also removed from their groups.", "Delete"))
        {
            return;
        }

        foreach (var row in rows)
        {
            _projects.Current.RemoveFixture(row.Fixture.Id);
        }

        _projects.MarkChanged();
        Reload();
    }

    /// <summary>Copies every selected fixture to the next free address (copies keep their groups).</summary>
    [RelayCommand(CanExecute = nameof(HasRowSelection))]
    private void Duplicate()
    {
        var copies = SelectedRows.Select(r => _projects.Current.DuplicateFixture(r.Fixture.Id)).ToList();
        _projects.MarkChanged();
        Reload();
        SelectFixtures(copies);
    }

    /// <summary>Moves every selected fixture, in table order, to the first free address of its universe.</summary>
    [RelayCommand(CanExecute = nameof(HasRowSelection))]
    private void AutoPatch()
    {
        var project = _projects.Current;
        var rows = SelectedRows;
        foreach (var row in rows)
        {
            if (PatchValidator.FindFreeAddress(project.Fixtures, project, row.Universe, Math.Max(1, row.ChannelCount), row.Fixture.Id) is { } free)
            {
                row.Fixture.StartAddress = free;
            }
        }

        _projects.MarkChanged();
        foreach (var row in rows)
        {
            row.RefreshAll();
        }

        Revalidate();
        RefreshGroups();
    }

    /// <summary>Moves the fixture in the Properties panel to the first free address of its universe.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AutoAddress()
    {
        var row = SelectedFixture!;
        if (PatchValidator.FindFreeAddress(_projects.Current.Fixtures, _projects.Current, row.Universe, Math.Max(1, row.ChannelCount), row.Fixture.Id) is { } free)
        {
            row.StartAddress = free;
        }
    }

    /// <summary>
    /// Edits the channel layout of the selected light. A custom layout is edited in place (every light using it follows);
    /// a built-in or imported profile stays unchanged and is copied into a new layout, which the chosen lights then use.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditChannelLayoutAsync()
    {
        var focused = SelectedFixture!;
        var project = _projects.Current;
        if (project.FindProfile(focused.Fixture.ProfileId) is not { } profile)
        {
            await _dialogs.ShowErrorAsync(
                "Edit channel layout",
                $"The profile of \"{focused.Name}\" is missing from the project. Pick a profile for it in the Properties panel first.");
            return;
        }

        if (ChannelLayout.IsCustom(profile))
        {
            var editor = new ChannelLayoutViewModel(_library, _projects, _dialogs, profile, focused.Fixture.ModeName, editSource: true);
            if (_dialogs.ShowChannelLayout(editor))
            {
                Reload();
            }

            return;
        }

        if (!await _dialogs.ConfirmAsync(
                "Edit channel layout",
                $"\"{profile.DisplayName}\" is a built-in profile and stays unchanged. Make a new layout from it that you can " +
                "change to match your light's channel order?",
                "Edit as new layout…"))
        {
            return;
        }

        var copy = new ChannelLayoutViewModel(_library, _projects, _dialogs, profile, focused.Fixture.ModeName, editSource: false);
        copy.SetApplyTargets(BuildApplyTargets(profile));
        if (!_dialogs.ShowChannelLayout(copy) || copy.SavedProfile is not { } saved)
        {
            return;
        }

        var switchIds = copy.FixturesToSwitch.ToHashSet();
        if (switchIds.Count == 0)
        {
            return;
        }

        var embedded = project.EmbedProfile(saved);
        var modeName = embedded.Modes.LastOrDefault()?.Name ?? string.Empty;
        foreach (var fixture in project.Fixtures.Where(f => switchIds.Contains(f.Id)))
        {
            fixture.ProfileId = embedded.Id;
            fixture.ModeName = modeName;
        }

        _projects.MarkChanged();
        Reload();
        SelectFixtures(project.Fixtures.Where(f => switchIds.Contains(f.Id)));

        // A different channel count can make footprints overlap: say so instead of applying it silently.
        var problems = PatchValidator.Validate(project.Fixtures, project)
            .Where(i => i.EntityId is { } id && switchIds.Contains(id) && i.Severity == ValidationSeverity.Error)
            .Select(i => "• " + i.Message)
            .ToList();
        if (problems.Count > 0)
        {
            await _dialogs.ShowInfoAsync(
                "Check the patch",
                $"{switchIds.Count} light(s) now use \"{embedded.DisplayName}\", but:{Environment.NewLine}" +
                string.Join(Environment.NewLine, problems.Take(8)) +
                (problems.Count > 8 ? $"{Environment.NewLine}… and {problems.Count - 8} more." : string.Empty) +
                $"{Environment.NewLine}{Environment.NewLine}Use Auto-patch or change their addresses.");
        }
    }

    /// <summary>"Use it for" choices: the selected lights of this profile, all of them, or none.</summary>
    private List<ChannelLayoutApplyTarget> BuildApplyTargets(FixtureProfile profile)
    {
        bool Uses(Fixture f) => string.Equals(f.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase);
        var selected = SelectedRows.Select(r => r.Fixture).Where(Uses).Select(f => f.Id).ToList();
        var all = _projects.Current.Fixtures.Where(Uses).Select(f => f.Id).ToList();

        var targets = new List<ChannelLayoutApplyTarget>();
        if (selected.Count > 0)
        {
            var label = selected.Count == 1
                ? $"Selected light \"{_projects.Current.FindFixture(selected[0])?.Name}\""
                : $"{selected.Count} selected lights";
            targets.Add(new ChannelLayoutApplyTarget(label, selected));
        }

        if (all.Count > selected.Count)
        {
            targets.Add(new ChannelLayoutApplyTarget($"All {all.Count} lights using {profile.Model}", all));
        }

        targets.Add(new ChannelLayoutApplyTarget("No lights (only save the layout)", []));
        return targets;
    }

    /// <summary>Selects the fixture occupying an address on the map.</summary>
    [RelayCommand]
    private void SelectAddress(int address)
    {
        var map = AddressMap.Build(_projects.Current.Fixtures, _projects.Current, MapUniverse);
        if (map.OwnersAt(address).FirstOrDefault() is { } owner)
        {
            SelectFixtures([owner]);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Search = string.Empty;
        UniverseFilter = AllUniverses;
    }

    private bool HasRowSelection() => SelectedFixture is not null || Fixtures.Any(r => r.IsSelected);

    // ───────────────────────────── Groups ─────────────────────────────

    /// <summary>Creates a group from the selected fixtures (or an empty one) and starts renaming it.</summary>
    [RelayCommand]
    private void NewGroup()
    {
        var project = _projects.Current;
        var group = project.CreateGroup(project.NextGroupName(), SelectedRows.Select(r => r.Fixture.Id));
        _projects.MarkChanged(affectsPlayback: false);
        RefreshGroups();
        if (Groups.FirstOrDefault(g => g.Group == group) is { } node)
        {
            node.IsSelected = true;
            node.BeginRename();
        }
    }

    /// <summary>Adds the selected fixtures to <paramref name="target"/> (from the Group menu) or the selected group.</summary>
    [RelayCommand(CanExecute = nameof(CanAddToGroup))]
    private void AddToGroup(GroupRowViewModel? target)
    {
        var group = (target ?? SelectedGroup)?.Group;
        if (group is null)
        {
            return;
        }

        if (_projects.Current.AddToGroup(group.Id, SelectedRows.Select(r => r.Fixture.Id)) > 0)
        {
            _projects.MarkChanged(affectsPlayback: false);
            RefreshGroups();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMember))]
    private void RemoveFromGroup()
    {
        var member = SelectedMember!;
        if (_projects.Current.RemoveFromGroup(member.Parent.Group.Id, member.Fixture.Id))
        {
            _projects.MarkChanged(affectsPlayback: false);
            SelectedGroupNode = member.Parent;
            RefreshGroups();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveMemberUp))]
    private void MoveMemberUp() => MoveMember(-1);

    [RelayCommand(CanExecute = nameof(CanMoveMemberDown))]
    private void MoveMemberDown() => MoveMember(1);

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private void RenameGroup()
    {
        SelectedGroup!.BeginRename();
    }

    /// <summary>Deletes the selected group after confirmation. Its fixtures stay patched.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private async Task DeleteGroupAsync()
    {
        var group = SelectedGroup!;
        var members = group.Members.Count == 0 ? "It is empty." : $"Its {group.MemberCountText} stay patched.";
        if (!await _dialogs.ConfirmAsync("Delete group", $"Delete group \"{group.Name}\"? {members}", "Delete"))
        {
            return;
        }

        _projects.Current.DeleteGroup(group.Group.Id);
        _projects.MarkChanged(affectsPlayback: false);
        SelectedGroupNode = null;
        RefreshGroups();
    }

    /// <summary>Del in the Groups tree: removes a member from its group, or deletes a group (with confirmation).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private async Task DeleteGroupNodeAsync()
    {
        if (SelectedMember is not null)
        {
            RemoveFromGroup();
        }
        else
        {
            await DeleteGroupAsync();
        }
    }

    private bool CanAddToGroup(GroupRowViewModel? target) => (target ?? SelectedGroup) is not null && HasRowSelection();

    private bool HasSelectedGroup() => SelectedGroup is not null;

    private bool HasSelectedMember() => SelectedMember is not null;

    private bool CanMoveMemberUp() => SelectedMember is { Position: > 1 };

    private bool CanMoveMemberDown() => SelectedMember is { } m && m.Position < m.Parent.Members.Count;

    private void MoveMember(int delta)
    {
        var member = SelectedMember!;
        if (_projects.Current.MoveInGroup(member.Parent.Group.Id, member.Fixture.Id, delta))
        {
            _projects.MarkChanged(affectsPlayback: false);
            RefreshGroups();
        }
    }

    private bool RenameGroupNode(GroupRowViewModel node, string name)
    {
        if (!_projects.Current.RenameGroup(node.Group.Id, name))
        {
            return false;
        }

        _projects.MarkChanged(affectsPlayback: false);
        UpdateGroupNames();
        return true;
    }

    private void OnGroupNodeSelected(object node)
    {
        SelectedGroupNode = node;
        if (_rebuildingGroups)
        {
            return;
        }

        switch (node)
        {
            case GroupRowViewModel group when group.Members.Count > 0:
                SelectFixtures(group.Members.Select(m => m.Fixture));
                break;
            case GroupMemberViewModel member:
                SelectFixtures([member.Fixture]);
                break;
        }
    }

    // ───────────────────────────── Refresh ─────────────────────────────

    /// <summary>Selects exactly these fixtures in the table (those hidden by the filter are skipped).</summary>
    private void SelectFixtures(IEnumerable<Fixture> fixtures)
    {
        var targets = fixtures.ToHashSet();
        var first = Fixtures.FirstOrDefault(r => targets.Contains(r.Fixture));
        if (first is null)
        {
            return;
        }

        // Setting SelectedItem makes the DataGrid select only that row; then add the rest.
        SelectedFixture = first;
        foreach (var row in Fixtures)
        {
            row.IsSelected = targets.Contains(row.Fixture);
        }

        TableSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Reload()
    {
        if (_reloading)
        {
            return;
        }

        _reloading = true;
        try
        {
            ReloadCore();
        }
        finally
        {
            _reloading = false;
        }
    }

    /// <summary>
    /// Rebuilds the table. When only the search or universe filter changed (same project revision, same library), the
    /// existing rows are kept and the table is updated in place: typing in the search box then only removes or inserts
    /// the rows that changed instead of regenerating every DataGrid row, the groups tree and the pickers.
    /// </summary>
    private void ReloadCore()
    {
        var contentChanged = _loadedRevision != _projects.Revision || _libraryChangedSinceLoad || _rowCache.Count == 0;
        _loadedRevision = _projects.Revision;
        _libraryChangedSinceLoad = false;
        var project = _projects.Current;
        var selected = SelectedRows.Select(r => r.Fixture).ToHashSet();
        var focused = SelectedFixture?.Fixture;

        if (contentChanged)
        {
            _profileOptions = BuildProfileOptions();
            OnPropertyChanged(nameof(ProfileOptions));
            RebuildUniverseLists();
            PruneRowCache(project);
        }

        var query = Search.Trim();
        var universe = UniverseFilter.Universe;
        var wanted = new List<FixtureRowViewModel>();
        foreach (var fixture in project.Fixtures.Where(f =>
                     (universe is null || f.Universe == universe) &&
                     (query.Length == 0 ||
                      f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      (project.FindProfile(f.ProfileId)?.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))))
        {
            if (!_rowCache.TryGetValue(fixture, out var row))
            {
                row = new FixtureRowViewModel(fixture, project, () => _profileOptions, OnFixtureEdited);
                row.PropertyChanged += OnRowPropertyChanged;
                _rowCache.Add(fixture, row);
            }

            wanted.Add(row);
        }

        SyncFixtureRows(wanted);

        SelectedFixture = Fixtures.FirstOrDefault(r => r.Fixture == focused) ?? Fixtures.FirstOrDefault();
        foreach (var row in Fixtures)
        {
            row.IsSelected = row == SelectedFixture || selected.Contains(row.Fixture);
        }

        TableSelectionChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsProjectEmpty));
        OnPropertyChanged(nameof(NoFilterResults));
        Revalidate();
        if (contentChanged)
        {
            RefreshGroups();
        }
        else
        {
            // Rows that were filtered out until now need their group column.
            UpdateGroupNames();
        }

        NotifySelectionCommands();
    }

    /// <summary>
    /// After the project content changed: keeps the rows of fixtures that are still patched (re-reading their values,
    /// which all come live from the model) and drops the rest. Adding 50 lights to a 500-light rig then inserts 50 rows
    /// instead of regenerating all 550 DataGrid rows. A different project (New/Open) starts from scratch.
    /// </summary>
    private void PruneRowCache(Project project)
    {
        if (!ReferenceEquals(_rowCacheProject, project))
        {
            foreach (var row in _rowCache.Values)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            _rowCache.Clear();
            Fixtures.Clear();
            _rowCacheProject = project;
            return;
        }

        var patched = new HashSet<Fixture>(project.Fixtures, ReferenceEqualityComparer.Instance);
        List<Fixture>? gone = null;
        foreach (var (fixture, row) in _rowCache)
        {
            if (patched.Contains(fixture))
            {
                row.RefreshAll();
            }
            else
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                (gone ??= []).Add(fixture);
            }
        }

        if (gone is not null)
        {
            foreach (var fixture in gone)
            {
                _rowCache.Remove(fixture);
            }
        }
    }

    /// <summary>
    /// Makes <see cref="Fixtures"/> equal to <paramref name="wanted"/> (both in project order) with removes and inserts
    /// only, so rows that stay keep their DataGrid row.
    /// </summary>
    private void SyncFixtureRows(List<FixtureRowViewModel> wanted)
    {
        var keep = new HashSet<FixtureRowViewModel>(wanted);
        for (var i = Fixtures.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Fixtures[i]))
            {
                Fixtures.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= Fixtures.Count || !ReferenceEquals(Fixtures[i], wanted[i]))
            {
                Fixtures.Insert(i, wanted[i]);
            }
        }
    }

    private void RebuildUniverseLists()
    {
        var project = _projects.Current;
        var mapUniverse = MapUniverse;
        Universes.Clear();
        foreach (var u in project.Universes)
        {
            Universes.Add(u);
        }

        MapUniverse = Universes.Contains(mapUniverse) ? mapUniverse : 1;
        OnPropertyChanged(nameof(MapUniverseSelection));

        var filter = UniverseFilter;
        UniverseFilters.Clear();
        UniverseFilters.Add(AllUniverses);
        foreach (var u in Universes)
        {
            UniverseFilters.Add(new UniverseFilterOption(u, $"Universe {u}"));
        }

        // Records compare by value, so the restored option matches the new instance in the list. Callers hold
        // _reloading, so this doesn't trigger another reload.
        UniverseFilter = filter is not null && UniverseFilters.Contains(filter) ? filter : AllUniverses;
        OnPropertyChanged(nameof(UniverseFilter));
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FixtureRowViewModel.IsSelected))
        {
            QueueSelectionCommands();
        }
    }

    /// <summary>
    /// Select All / a shift-click range flips hundreds of rows at once: refresh the commands once per dispatcher turn
    /// instead of once per row (each refresh re-evaluates every command's CanExecute).
    /// </summary>
    private void QueueSelectionCommands()
    {
        if (_selectionCommandsPending)
        {
            return;
        }

        if (SynchronizationContext.Current is not { } context)
        {
            NotifySelectionCommands();
            return;
        }

        _selectionCommandsPending = true;
        context.Post(
            _ =>
            {
                _selectionCommandsPending = false;
                NotifySelectionCommands();
            },
            null);
    }

    private void NotifySelectionCommands()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        AutoPatchCommand.NotifyCanExecuteChanged();
        AddToGroupCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// One Properties field of a fixture changed. Only what that field affects is refreshed: notes touch nothing else
    /// (and never the engine); the other fields re-validate the patch and update this fixture's entries in the Groups
    /// tree in place instead of rebuilding the tree.
    /// </summary>
    private void OnFixtureEdited(FixtureRowViewModel row, string property)
    {
        if (property == nameof(FixtureRowViewModel.Notes))
        {
            _projects.MarkChanged(affectsPlayback: false);
            return;
        }

        _projects.MarkChanged();
        if (!Universes.Contains(row.Universe))
        {
            _reloading = true;
            try
            {
                RebuildUniverseLists();
            }
            finally
            {
                _reloading = false;
            }
        }

        Revalidate();
        RefreshGroupMembers(row.Fixture);
    }

    /// <summary>Updates the name and address of <paramref name="fixture"/> wherever it appears in the Groups tree.</summary>
    private void RefreshGroupMembers(Fixture fixture)
    {
        string? addressText = null;
        foreach (var group in Groups)
        {
            foreach (var member in group.Members)
            {
                if (ReferenceEquals(member.Fixture, fixture))
                {
                    addressText ??= MemberAddressText(_projects.Current, fixture);
                    member.Refresh(addressText);
                }
            }
        }
    }

    /// <summary>"U1 · 1–8" for a group member (just the start address when the footprint is unknown or one channel).</summary>
    private static string MemberAddressText(Project project, Fixture fixture)
    {
        var channels = project.FindProfile(fixture.ProfileId)?.FindMode(fixture.ModeName)?.ChannelCount ?? 0;
        var range = channels <= 1
            ? fixture.StartAddress.ToString()
            : $"{fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, channels)}";
        return $"U{fixture.Universe} · {range}";
    }

    /// <summary>Re-runs patch validation and pushes results to rows, field messages, the issue list and the map.</summary>
    private void Revalidate()
    {
        var issues = ValidatePatch(force: true);
        var byFixture = issues.Where(i => i.EntityId is not null).ToLookup(i => i.EntityId!.Value);
        foreach (var row in Fixtures)
        {
            var own = byFixture[row.Fixture.Id].ToList();
            row.WorstIssue = own.Count == 0 ? null : own.Max(i => i.Severity);
            row.IssueText = string.Join(Environment.NewLine, own.Select(i => i.Message));
            var address = own.Where(i => AddressCodes.Contains(i.Code)).ToList();
            row.AddressIssue = string.Join(Environment.NewLine, address.Select(i => i.Message));
            row.AddressIssueIsError = address.Any(i => i.Severity == ValidationSeverity.Error);
            row.UniverseIssue = string.Join(Environment.NewLine, own.Where(i => i.Code == UniverseCode).Select(i => i.Message));
        }

        UpdateSelectedIssues(issues);
        UpdateAddressMap();
    }

    /// <summary>
    /// Patch issues of the current project. Selecting a row only needs the selected fixture's issues, so the last result
    /// is reused while the project revision is unchanged (validation is O(fixtures) with string building per issue).
    /// </summary>
    private IReadOnlyList<ValidationIssue> ValidatePatch(bool force = false)
    {
        if (force || _issues is null || _issuesRevision != _projects.Revision)
        {
            _issues = PatchValidator.Validate(_projects.Current.Fixtures, _projects.Current);
            _issuesRevision = _projects.Revision;
        }

        return _issues;
    }

    private void UpdateSelectedIssues(IReadOnlyList<ValidationIssue>? issues = null)
    {
        issues ??= ValidatePatch();
        SelectedIssues.Clear();
        if (SelectedFixture is null)
        {
            return;
        }

        foreach (var issue in issues
                     .Where(i => i.EntityId == SelectedFixture.Fixture.Id && !AddressCodes.Contains(i.Code) && i.Code != UniverseCode)
                     .OrderByDescending(i => i.Severity))
        {
            SelectedIssues.Add(issue);
        }
    }

    private void UpdateAddressMap()
    {
        var map = AddressMap.Build(_projects.Current.Fixtures, _projects.Current, MapUniverse);
        var selected = SelectedFixture?.Fixture;
        foreach (var cell in AddressCells)
        {
            var owners = map.OwnersAt(cell.Address);
            cell.State = owners.Count switch
            {
                0 => AddressCellState.Free,
                1 => AddressCellState.Used,
                _ => AddressCellState.Overlap,
            };
            cell.IsSelected = selected is not null && owners.Contains(selected);
            cell.ToolTip = owners.Count switch
            {
                0 => $"Address {cell.Address} · free",
                1 => $"Address {cell.Address} · {owners[0].Name}",
                _ => $"Address {cell.Address} · overlap: {string.Join(", ", owners.Select(o => o.Name))}",
            };
        }

        // The universe is already shown in the map's combo box, so the summary only counts channels.
        MapSummary = $"{map.UsedCount} of {DmxAddress.UniverseSize} used" +
                     (map.OverlapCount > 0 ? $" · {map.OverlapCount} overlapping" : string.Empty);
    }

    /// <summary>Rebuilds the Groups tree from the project, keeping expansion and the selected node.</summary>
    private void RefreshGroups()
    {
        var project = _projects.Current;
        var fixturesById = new Dictionary<Guid, Fixture>(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            fixturesById.TryAdd(fixture.Id, fixture);
        }

        var selectedGroupId = SelectedGroup?.Group.Id;
        var selectedMemberId = SelectedMember?.Fixture.Id;
        var collapsed = Groups.Where(g => !g.IsExpanded).Select(g => g.Group.Id).ToHashSet();

        _rebuildingGroups = true;
        try
        {
            Groups.Clear();
            object? restore = null;
            foreach (var group in project.Groups)
            {
                var node = new GroupRowViewModel(group, OnGroupNodeSelected, RenameGroupNode)
                {
                    IsExpanded = !collapsed.Contains(group.Id),
                };
                var position = 1;
                foreach (var fixture in group.FixtureIds.Select(fixturesById.GetValueOrDefault).OfType<Fixture>())
                {
                    var member = new GroupMemberViewModel(node, fixture, position++, MemberAddressText(project, fixture), OnGroupNodeSelected);
                    node.Members.Add(member);
                    if (group.Id == selectedGroupId && fixture.Id == selectedMemberId)
                    {
                        restore = member;
                    }
                }

                Groups.Add(node);
                if (group.Id == selectedGroupId)
                {
                    restore ??= node;
                }
            }

            SelectedGroupNode = restore;
            switch (restore)
            {
                case GroupRowViewModel g:
                    g.IsSelected = true;
                    break;
                case GroupMemberViewModel m:
                    m.Parent.IsExpanded = true;
                    m.IsSelected = true;
                    break;
            }
        }
        finally
        {
            _rebuildingGroups = false;
        }

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasNoGroups));
        UpdateGroupNames();
    }

    /// <summary>Group column of every row; one pass over the groups instead of a group scan per row.</summary>
    private void UpdateGroupNames()
    {
        var namesByFixture = new Dictionary<Guid, List<string>>();
        foreach (var group in _projects.Current.Groups)
        {
            foreach (var fixtureId in group.FixtureIds.Distinct())
            {
                if (!namesByFixture.TryGetValue(fixtureId, out var names))
                {
                    names = [];
                    namesByFixture.Add(fixtureId, names);
                }

                names.Add(group.Name);
            }
        }

        foreach (var row in Fixtures)
        {
            row.GroupNames = namesByFixture.TryGetValue(row.Fixture.Id, out var names) ? string.Join(", ", names) : string.Empty;
        }
    }

    /// <summary>Library profiles plus any profile embedded in the project that the library doesn't have.</summary>
    private IReadOnlyList<FixtureProfile> BuildProfileOptions()
    {
        var library = _library.Library.Profiles;
        var extra = _projects.Current.Profiles.Where(p => library.All(l => !string.Equals(l.Id, p.Id, StringComparison.OrdinalIgnoreCase)));
        return library.Concat(extra).OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
