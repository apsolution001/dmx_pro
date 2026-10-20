using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DmxPro.App.ViewModels;

/// <summary>
/// The scene editor's "General" tab: only which lights the scene uses (add by fixture or group, remove, find).
/// Channel levels are set on the Channels tab; the fade times are in the editor toolbar. All edits go through the
/// owning <see cref="SceneEditorViewModel"/>, which holds the draft.
/// </summary>
public sealed partial class SceneGeneralTabViewModel : SceneEditorTab
{
    private readonly SceneEditorViewModel _owner;
    private readonly List<SceneLightRow> _allLights = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private SceneLightRow? _selectedLight;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _addSearchText = string.Empty;

    internal SceneGeneralTabViewModel(SceneEditorViewModel owner)
    {
        _owner = owner;
    }

    public override string Header => "General";

    public override string IconKind => "Settings";

    /// <summary>The lights in the scene (patch order) that match <see cref="SearchText"/>.</summary>
    public ObservableCollection<SceneLightRow> Lights { get; } = [];

    /// <summary>Every patched fixture; ticking <see cref="SceneFixtureChoice.IsInScene"/> adds/removes it.</summary>
    public ObservableCollection<SceneFixtureChoice> AvailableFixtures { get; } = [];

    /// <summary>Every group; ticking adds/removes all its members. Ticked when all members are in the scene.</summary>
    public ObservableCollection<SceneFixtureChoice> AvailableGroups { get; } = [];

    /// <summary><see cref="AvailableFixtures"/> matching <see cref="AddSearchText"/> (the Add flyout list).</summary>
    public ObservableCollection<SceneFixtureChoice> VisibleFixtures { get; } = [];

    /// <summary><see cref="AvailableGroups"/> matching <see cref="AddSearchText"/> (the Add flyout list).</summary>
    public ObservableCollection<SceneFixtureChoice> VisibleGroups { get; } = [];

    public int LightCount => _allLights.Count;

    /// <summary>True when the scene has no lights at all (shows the "add lights" empty state).</summary>
    public bool HasNoLights => _allLights.Count == 0;

    /// <summary>True when the scene has lights but none match the search.</summary>
    public bool HasNoSearchMatches => _allLights.Count > 0 && Lights.Count == 0;

    /// <summary>"3 lights" for the toolbar.</summary>
    public string LightCountText => _allLights.Count == 1 ? "1 light" : $"{_allLights.Count} lights";

    public bool HasNoAddMatches => VisibleFixtures.Count == 0 && VisibleGroups.Count == 0
        && (AvailableFixtures.Count > 0 || AvailableGroups.Count > 0);

    private bool HasSelectedLight => SelectedLight is not null;

    /// <summary>Replaces the scene's lights (called by the owner after every add/remove).</summary>
    internal void SetLights(IEnumerable<SceneLightRow> lights)
    {
        var selectedId = SelectedLight?.FixtureId;
        _allLights.Clear();
        _allLights.AddRange(lights);
        ApplySearch();
        SelectedLight = selectedId is { } id ? Lights.FirstOrDefault(l => l.FixtureId == id) : null;
        OnPropertyChanged(nameof(LightCount));
        OnPropertyChanged(nameof(LightCountText));
        OnPropertyChanged(nameof(HasNoLights));
    }

    /// <summary>Rebuilds the Add flyout lists (called once the owner filled the available fixtures and groups).</summary>
    internal void ApplyAddSearch()
    {
        Filter(AvailableGroups, VisibleGroups, AddSearchText);
        Filter(AvailableFixtures, VisibleFixtures, AddSearchText);
        OnPropertyChanged(nameof(HasNoAddMatches));
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    partial void OnAddSearchTextChanged(string value) => ApplyAddSearch();

    private static bool Matches(string key, string search) =>
        string.IsNullOrWhiteSpace(search) || key.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private static void Filter(IEnumerable<SceneFixtureChoice> source, ObservableCollection<SceneFixtureChoice> target, string search)
    {
        target.Clear();
        foreach (var choice in source)
        {
            if (Matches($"{choice.Name} {choice.Detail}", search))
            {
                target.Add(choice);
            }
        }
    }

    private void ApplySearch()
    {
        var selected = SelectedLight;
        Lights.Clear();
        foreach (var light in _allLights)
        {
            if (Matches(light.SearchKey, SearchText))
            {
                Lights.Add(light);
            }
        }

        if (selected is not null && !Lights.Contains(selected))
        {
            SelectedLight = null;
        }

        OnPropertyChanged(nameof(HasNoSearchMatches));
    }

    /// <summary>Removes every selected light (Ctrl/Shift multi-select), or the current one.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLight))]
    private void RemoveSelected()
    {
        var ids = Lights.Where(l => l.IsSelected).Select(l => l.FixtureId).ToList();
        if (ids.Count == 0 && SelectedLight is { } light)
        {
            ids.Add(light.FixtureId);
        }

        if (ids.Count > 0)
        {
            _owner.SetFixturesInScene(ids, false);
        }
    }
}
