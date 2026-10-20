using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// The "Fixtures" panel of the effect and sequence editors: the fixtures the function drives, in order (order matters
/// for per-fixture offsets), plus an "Add" flyout with a search box, the groups and every patched fixture (ticked = in
/// the function). Remove and move up/down work on the selected fixture. Every change is reported through the callback
/// with the fixture ids that were added and removed (both empty for a reorder).
/// </summary>
public sealed partial class FixtureListEditorViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action<IReadOnlyList<Guid>, IReadOnlyList<Guid>> _onChanged;
    private readonly Dictionary<Guid, SceneFixtureChoice> _choices = [];
    private readonly List<Guid> _ids = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private FixtureListItem? _selectedItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public FixtureListEditorViewModel(Project project, Action<IReadOnlyList<Guid>, IReadOnlyList<Guid>> onChanged)
    {
        _project = project;
        _onChanged = onChanged;
        RefreshPatch();
    }

    /// <summary>(Re)builds the Add lists from the project's fixtures and groups; ticks follow the current selection.</summary>
    public void RefreshPatch()
    {
        _choices.Clear();
        AvailableFixtures.Clear();
        AvailableGroups.Clear();
        var inList = _ids.ToHashSet();
        foreach (var fixture in _project.Fixtures)
        {
            var choice = new SceneFixtureChoice(fixture.Id, fixture.Name, Detail(fixture), false, [fixture.Id], OnChoiceToggled);
            choice.SetInSceneSilently(inList.Contains(fixture.Id));
            _choices[fixture.Id] = choice;
            AvailableFixtures.Add(choice);
        }

        foreach (var group in _project.Groups)
        {
            var members = group.FixtureIds.Where(_choices.ContainsKey).ToList();
            var count = members.Count == 1 ? "1 fixture" : $"{members.Count} fixtures";
            var choice = new SceneFixtureChoice(group.Id, group.Name, count, true, members, OnChoiceToggled);
            choice.SetInSceneSilently(members.Count > 0 && members.All(inList.Contains));
            AvailableGroups.Add(choice);
        }

        ApplyFilter();
        Rebuild(SelectedItem?.Id);
        OnPropertyChanged(nameof(HasNoPatchedFixtures));
        OnPropertyChanged(nameof(HasNoGroups));
    }

    /// <summary>The fixtures in the function, in order.</summary>
    public ObservableCollection<FixtureListItem> Items { get; } = [];

    /// <summary>Every patched fixture (Add flyout); ticked = in the function.</summary>
    public ObservableCollection<SceneFixtureChoice> AvailableFixtures { get; } = [];

    /// <summary><see cref="AvailableFixtures"/> matching <see cref="SearchText"/>.</summary>
    public ObservableCollection<SceneFixtureChoice> FilteredFixtures { get; } = [];

    /// <summary>Every group; ticked when all its members are in the function.</summary>
    public ObservableCollection<SceneFixtureChoice> AvailableGroups { get; } = [];

    public bool HasNoItems => Items.Count == 0;

    public bool HasNoPatchedFixtures => AvailableFixtures.Count == 0;

    public bool HasNoGroups => AvailableGroups.Count == 0;

    /// <summary>The fixture ids in order.</summary>
    public IReadOnlyList<Guid> FixtureIds => _ids;

    /// <summary>"3 fixtures".</summary>
    public string CountText => Items.Count == 1 ? "1 fixture" : $"{Items.Count} fixtures";

    /// <summary>Warning about fixtures with unverified profiles in the list; null when all are verified.</summary>
    public string? UnverifiedWarning
    {
        get
        {
            var fixtures = FixturesById();
            var names = _ids
                .Select(id => fixtures.GetValueOrDefault(id))
                .Where(f => f is not null)
                .Select(f => _project.FindProfile(f!.ProfileId))
                .Where(p => p is { Verified: false })
                .Select(p => p!.DisplayName)
                .Distinct()
                .ToList();
            return names.Count == 0
                ? null
                : $"Not verified on a real fixture: {string.Join(", ", names)}. Check the channel meanings before using this in a show.";
        }
    }

    /// <summary>Replaces the list without reporting a change (editor open / revert).</summary>
    public void Load(IEnumerable<Guid> fixtureIds)
    {
        _ids.Clear();
        var seen = new HashSet<Guid>();
        foreach (var id in fixtureIds)
        {
            if (seen.Add(id))
            {
                _ids.Add(id);
            }
        }

        Rebuild(null);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void RemoveSelected()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var index = _ids.IndexOf(item.Id);
        _ids.Remove(item.Id);
        Rebuild(index < _ids.Count ? _ids[Math.Max(index, 0)] : _ids.LastOrDefault());
        _onChanged([], [item.Id]);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private bool HasSelectedItem => SelectedItem is not null;

    private bool CanMoveUp => SelectedItem is { } item && _ids.IndexOf(item.Id) > 0;

    private bool CanMoveDown => SelectedItem is { } item && _ids.IndexOf(item.Id) is var i && i >= 0 && i < _ids.Count - 1;

    private void Move(int offset)
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        FunctionEditing.MoveItem(_ids, _ids.IndexOf(item.Id), offset);
        Rebuild(item.Id);
        _onChanged([], []);
    }

    private void OnChoiceToggled(SceneFixtureChoice choice, bool include)
    {
        var added = new List<Guid>();
        var removed = new List<Guid>();
        var inList = _ids.ToHashSet();
        foreach (var id in choice.FixtureIds)
        {
            if (include && inList.Add(id))
            {
                _ids.Add(id);
                added.Add(id);
            }
            else if (!include && _ids.Remove(id))
            {
                removed.Add(id);
            }
        }

        Rebuild(SelectedItem?.Id);
        if (added.Count > 0 || removed.Count > 0)
        {
            _onChanged(added, removed);
        }
    }

    private void Rebuild(Guid? select)
    {
        Items.Clear();
        var fixtures = FixturesById();
        for (var i = 0; i < _ids.Count; i++)
        {
            var id = _ids[i];
            if (fixtures.TryGetValue(id, out var fixture))
            {
                var unverified = _project.FindProfile(fixture.ProfileId) is { Verified: false };
                Items.Add(new FixtureListItem(id, i + 1, fixture.Name, Detail(fixture), unverified, false));
            }
            else
            {
                Items.Add(new FixtureListItem(id, i + 1, "(missing fixture)", "Deleted from the patch — remove it here", false, true));
            }
        }

        var inList = _ids.ToHashSet();
        foreach (var (id, choice) in _choices)
        {
            choice.SetInSceneSilently(inList.Contains(id));
        }

        foreach (var group in AvailableGroups)
        {
            group.SetInSceneSilently(group.FixtureIds.Count > 0 && group.FixtureIds.All(inList.Contains));
        }

        SelectedItem = select is { } keep ? Items.FirstOrDefault(i => i.Id == keep) : null;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(UnverifiedWarning));
    }

    private void ApplyFilter()
    {
        var text = SearchText?.Trim() ?? string.Empty;
        FilteredFixtures.Clear();
        foreach (var choice in AvailableFixtures)
        {
            if (text.Length == 0
                || choice.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || choice.Detail.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFixtures.Add(choice);
            }
        }
    }

    private string Detail(Fixture fixture)
    {
        var profile = _project.FindProfile(fixture.ProfileId);
        var channels = _project.ResolveFixtureChannels(fixture).Count;
        var address = channels <= 1
            ? $"U{fixture.Universe} · {fixture.StartAddress}"
            : $"U{fixture.Universe} · {fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, channels)}";
        return $"{address} · {(profile is null ? "missing profile" : profile.DisplayName)}";
    }

    /// <summary>Fixtures by id (first wins, like <see cref="Project.FindFixture"/>): effect lists can hold hundreds of lights.</summary>
    private Dictionary<Guid, Fixture> FixturesById()
    {
        var result = new Dictionary<Guid, Fixture>(_project.Fixtures.Count);
        foreach (var fixture in _project.Fixtures)
        {
            result.TryAdd(fixture.Id, fixture);
        }

        return result;
    }
}
