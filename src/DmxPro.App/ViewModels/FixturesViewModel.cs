using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Fixtures page (QLC+ style): fixture table, Properties panel, Groups panel and the universe address map.
/// </summary>
public sealed partial class FixturesViewModel : PageViewModel
{
    private readonly ProjectService _projects;
    private readonly ProfileLibraryService _library;
    private readonly IDialogService _dialogs;
    private IReadOnlyList<FixtureProfile> _profileOptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(AutoAddressCommand))]
    private FixtureRowViewModel? _selectedFixture;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private int _mapUniverse = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteGroupCommand), nameof(AddToGroupCommand))]
    private GroupRowViewModel? _selectedGroup;

    [ObservableProperty]
    private string _mapSummary = string.Empty;

    public FixturesViewModel(ProjectService projects, ProfileLibraryService library, IDialogService dialogs)
    {
        _projects = projects;
        _library = library;
        _dialogs = dialogs;
        for (var a = DmxAddress.Min; a <= DmxAddress.Max; a++)
        {
            AddressCells.Add(new AddressCellViewModel(a));
        }

        _projects.ProjectChanged += (_, _) => Reload();
        _library.LibraryChanged += (_, _) => _profileOptions = BuildProfileOptions();
        Reload();
    }

    public override string Title => "Fixtures";

    public override string Subtitle => "Patch your lights: profile, mode, universe and start address.";

    public ObservableCollection<FixtureRowViewModel> Fixtures { get; } = [];

    public ObservableCollection<GroupRowViewModel> Groups { get; } = [];

    public ObservableCollection<AddressCellViewModel> AddressCells { get; } = [];

    public ObservableCollection<int> Universes { get; } = [];

    /// <summary>Problems of the selected fixture, shown above its properties.</summary>
    public ObservableCollection<ValidationIssue> SelectedIssues { get; } = [];

    public IReadOnlyList<FixtureProfile> ProfileOptions => _profileOptions;

    public bool HasSelection => SelectedFixture is not null;

    public override void OnNavigatedTo() => Reload();

    partial void OnSelectedFixtureChanged(FixtureRowViewModel? value)
    {
        if (value is not null && value.Universe != MapUniverse && Universes.Contains(value.Universe))
        {
            MapUniverse = value.Universe;
        }

        UpdateSelectedIssues();
        UpdateAddressMap();
    }

    partial void OnSearchChanged(string value) => Reload();

    partial void OnMapUniverseChanged(int value) => UpdateAddressMap();

    [RelayCommand]
    private void AddFixture()
    {
        var dialog = new AddFixtureViewModel(_profileOptions, _projects.Current) { Universe = MapUniverse };
        if (!_dialogs.ShowAddFixture(dialog))
        {
            return;
        }

        var added = dialog.Apply();
        _projects.MarkChanged();
        Reload();
        SelectedFixture = Fixtures.FirstOrDefault(r => r.Fixture == added[^1]);
    }

    [RelayCommand]
    private async Task DeleteAsync(IList? selection)
    {
        var rows = selection?.OfType<FixtureRowViewModel>().ToList() ?? [];
        if (rows.Count == 0 && SelectedFixture is not null)
        {
            rows.Add(SelectedFixture);
        }

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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var copy = _projects.Current.DuplicateFixture(SelectedFixture!.Fixture.Id);
        _projects.MarkChanged();
        Reload();
        SelectedFixture = Fixtures.FirstOrDefault(r => r.Fixture == copy);
    }

    /// <summary>Moves the selected fixture to the first free address of its universe.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AutoAddress()
    {
        var row = SelectedFixture!;
        if (PatchValidator.FindFreeAddress(_projects.Current.Fixtures, _projects.Current, row.Universe, Math.Max(1, row.ChannelCount), row.Fixture.Id) is { } free)
        {
            row.StartAddress = free;
        }
    }

    [RelayCommand]
    private void NewGroup(IList? selection)
    {
        var ids = selection?.OfType<FixtureRowViewModel>().Select(r => r.Fixture.Id).ToList() ?? [];
        var group = _projects.Current.CreateGroup($"Group {_projects.Current.Groups.Count + 1}", ids);
        _projects.MarkChanged();
        RefreshGroups();
        SelectedGroup = Groups.FirstOrDefault(g => g.Group == group);
    }

    [RelayCommand(CanExecute = nameof(CanEditGroup))]
    private void AddToGroup(IList? selection)
    {
        var ids = selection?.OfType<FixtureRowViewModel>().Select(r => r.Fixture.Id) ?? [];
        var group = SelectedGroup!.Group;
        foreach (var id in ids.Where(id => !group.FixtureIds.Contains(id)))
        {
            group.FixtureIds.Add(id);
        }

        _projects.MarkChanged();
        RefreshGroups();
    }

    [RelayCommand(CanExecute = nameof(CanEditGroup))]
    private void DeleteGroup()
    {
        _projects.Current.Groups.Remove(SelectedGroup!.Group);
        _projects.MarkChanged();
        RefreshGroups();
    }

    /// <summary>Selects the fixture occupying an address on the map.</summary>
    [RelayCommand]
    private void SelectAddress(int address)
    {
        var map = AddressMap.Build(_projects.Current.Fixtures, _projects.Current, MapUniverse);
        if (map.OwnersAt(address).FirstOrDefault() is { } owner)
        {
            SelectedFixture = Fixtures.FirstOrDefault(r => r.Fixture == owner);
        }
    }

    private bool CanEditGroup() => SelectedGroup is not null;

    private void Reload()
    {
        _profileOptions = BuildProfileOptions();
        OnPropertyChanged(nameof(ProfileOptions));
        var selected = SelectedFixture?.Fixture;
        var query = Search.Trim();
        var project = _projects.Current;

        Fixtures.Clear();
        foreach (var fixture in project.Fixtures.Where(f => query.Length == 0 ||
                     f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     (project.FindProfile(f.ProfileId)?.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            Fixtures.Add(new FixtureRowViewModel(fixture, project, () => _profileOptions, OnFixtureEdited));
        }

        Universes.Clear();
        foreach (var u in project.Universes)
        {
            Universes.Add(u);
        }

        if (!Universes.Contains(MapUniverse))
        {
            MapUniverse = 1;
        }

        SelectedFixture = Fixtures.FirstOrDefault(r => r.Fixture == selected) ?? Fixtures.FirstOrDefault();
        Revalidate();
        RefreshGroups();
    }

    private void OnFixtureEdited(FixtureRowViewModel row)
    {
        _projects.MarkChanged();
        if (!Universes.Contains(row.Universe))
        {
            Universes.Add(row.Universe);
        }

        Revalidate();
    }

    /// <summary>Re-runs patch validation and pushes results to rows, the issue list and the map.</summary>
    private void Revalidate()
    {
        var issues = PatchValidator.Validate(_projects.Current.Fixtures, _projects.Current);
        foreach (var row in Fixtures)
        {
            var own = issues.Where(i => i.EntityId == row.Fixture.Id).ToList();
            row.WorstIssue = own.Count == 0 ? null : own.Max(i => i.Severity);
            row.IssueText = string.Join(Environment.NewLine, own.Select(i => i.Message));
        }

        UpdateSelectedIssues(issues);
        UpdateAddressMap();
    }

    private void UpdateSelectedIssues(IReadOnlyList<ValidationIssue>? issues = null)
    {
        issues ??= PatchValidator.Validate(_projects.Current.Fixtures, _projects.Current);
        SelectedIssues.Clear();
        if (SelectedFixture is null)
        {
            return;
        }

        foreach (var issue in issues.Where(i => i.EntityId == SelectedFixture.Fixture.Id).OrderByDescending(i => i.Severity))
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
            cell.ToolTip = owners.Count == 0 ? $"{cell.Address} · free" : $"{cell.Address} · {string.Join(", ", owners.Select(o => o.Name))}";
        }

        MapSummary = $"Universe {MapUniverse}: {map.UsedCount} of 512 channels used" +
                     (map.OverlapCount > 0 ? $" · {map.OverlapCount} overlapping" : string.Empty);
    }

    private void RefreshGroups()
    {
        var project = _projects.Current;
        var selected = SelectedGroup?.Group;
        Groups.Clear();
        foreach (var group in project.Groups)
        {
            var members = string.Join(", ", group.FixtureIds.Select(id => project.FindFixture(id)?.Name).OfType<string>());
            Groups.Add(new GroupRowViewModel(group, members.Length == 0 ? "(empty)" : members, () =>
            {
                _projects.MarkChanged();
                UpdateGroupNames();
            }));
        }

        SelectedGroup = Groups.FirstOrDefault(g => g.Group == selected);
        UpdateGroupNames();
    }

    private void UpdateGroupNames()
    {
        var groups = _projects.Current.Groups;
        foreach (var row in Fixtures)
        {
            row.GroupNames = string.Join(", ", groups.Where(g => g.FixtureIds.Contains(row.Fixture.Id)).Select(g => g.Name));
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
