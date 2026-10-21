using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Patching;

namespace DmxPro.App.ViewModels;

/// <summary>
/// The scene editor's "General" tab: only which lights the scene uses (add / choose lights in the modal
/// <see cref="FixturePickerViewModel"/> dialog by single light or group, remove, find).
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

    internal SceneGeneralTabViewModel(SceneEditorViewModel owner)
    {
        _owner = owner;
    }

    public override string Header => "General";

    public override string IconKind => "Settings";

    /// <summary>The lights in the scene (patch order) that match <see cref="SearchText"/>.</summary>
    public ObservableCollection<SceneLightRow> Lights { get; } = [];

    /// <summary>
    /// Opens the "Choose lights" dialog modally; returns true on OK. Set by the view (the editors are created without
    /// DI), or by tests. While null, <see cref="ChooseLightsCommand"/> does nothing.
    /// </summary>
    public Func<FixturePickerViewModel, bool>? ShowPicker { get; set; }

    /// <summary>Every patched fixture; ticking <see cref="SceneFixtureChoice.IsInScene"/> adds/removes it.</summary>
    public ObservableCollection<SceneFixtureChoice> AvailableFixtures { get; } = [];

    /// <summary>Every group; ticking adds/removes all its members. Ticked when all members are in the scene.</summary>
    public ObservableCollection<SceneFixtureChoice> AvailableGroups { get; } = [];

    public int LightCount => _allLights.Count;

    /// <summary>True when the scene has no lights at all (shows the "add lights" empty state).</summary>
    public bool HasNoLights => _allLights.Count == 0;

    /// <summary>True when the scene has lights but none match the search.</summary>
    public bool HasNoSearchMatches => _allLights.Count > 0 && Lights.Count == 0;

    /// <summary>"3 lights" for the toolbar.</summary>
    public string LightCountText => _allLights.Count == 1 ? "1 light" : $"{_allLights.Count} lights";

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

    /// <summary>
    /// Called by the owner after it rebuilt <see cref="AvailableFixtures"/> and <see cref="AvailableGroups"/>. Nothing
    /// to filter any more: lights are picked in the Choose lights dialog, which reads the patch when it opens.
    /// </summary>
    internal void ApplyAddSearch()
    {
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    private static bool Matches(string key, string search) =>
        string.IsNullOrWhiteSpace(search) || key.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);

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

    /// <summary>
    /// Opens the "Choose lights" dialog with the scene's lights picked; on OK, newly picked lights are added (patch
    /// defaults plus the colour of the lights already in the scene) and unpicked ones removed.
    /// </summary>
    [RelayCommand]
    private void ChooseLights()
    {
        if (ShowPicker is not { } show)
        {
            return;
        }

        var current = _allLights.Select(l => l.FixtureId).ToList();
        var picker = FixturePickerViewModel.FromProject(_owner.PatchProject, current, "Choose lights", ordered: false);
        if (!show(picker))
        {
            return;
        }

        var result = picker.Result;
        var removed = FixtureSelection.Added(result, current);
        var added = FixtureSelection.Added(current, result);
        if (removed.Count > 0)
        {
            _owner.SetFixturesInScene(removed, false);
        }

        if (added.Count > 0)
        {
            _owner.SetFixturesInScene(added, true);
        }
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
