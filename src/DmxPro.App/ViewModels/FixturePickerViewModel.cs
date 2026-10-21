using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels;

/// <summary>
/// The modal "Choose lights" dialog of the function editors: one picked set of lights, edited two ways. The Lights
/// tab lists every patched light (click, Shift+click range, Ctrl+click toggle, Ctrl+A, Select all / Clear); the Groups
/// tab ticks whole groups (ticking picks every member). Lights the function already uses start picked; on OK
/// <see cref="Result"/> is the function's new list: old order kept, unpicked lights removed, new ones appended in
/// patch order (<see cref="FixtureSelection.Merge"/>).
/// </summary>
public sealed partial class FixturePickerViewModel : ObservableObject
{
    private readonly List<FixturePickerLight> _lights = [];
    private readonly List<Guid> _patchOrder = [];
    private readonly List<Guid> _existing = [];
    private readonly HashSet<Guid> _picked = [];
    private readonly Dictionary<Guid, FixturePickerLight> _byId = [];
    private bool _rebuildingList;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <param name="title">Window title, e.g. "Choose lights · Rainbow chase".</param>
    /// <param name="ordered">The function plays its lights in list order (effects, sequences); false for a scene.</param>
    /// <param name="lights">Every patched light, in patch order.</param>
    /// <param name="groups">Every group as (id, name, patched member ids).</param>
    /// <param name="existing">The function's current fixture ids, in order (also those no longer patched).</param>
    public FixturePickerViewModel(
        string title,
        bool ordered,
        IEnumerable<FixturePickerLight> lights,
        IEnumerable<(Guid Id, string Name, IReadOnlyList<Guid> MemberIds)> groups,
        IEnumerable<Guid> existing)
    {
        Title = title;
        RemovalNote = ordered
            ? "Lights that are not selected are removed from this function. New lights go to the end of its order."
            : "Lights that are not selected are removed from this scene.";
        _existing.AddRange(existing);
        foreach (var light in lights)
        {
            if (_byId.TryAdd(light.Id, light))
            {
                _lights.Add(light);
                _patchOrder.Add(light.Id);
                if (light.IsPicked)
                {
                    _picked.Add(light.Id);
                }
            }
        }

        foreach (var (id, name, members) in groups)
        {
            var patched = members.Where(_byId.ContainsKey).Distinct().ToList();
            Groups.Add(new FixturePickerGroup(id, name, patched, OnGroupToggled));
        }

        RefreshGroupStates();
        ApplySearch();
    }

    /// <summary>
    /// Raised after the picked set changed from the ViewModel side (Select all, Clear, a group tick, a search): the
    /// view copies <see cref="FixturePickerLight.IsPicked"/> of <see cref="VisibleLights"/> into the list selection.
    /// </summary>
    public event EventHandler? SelectionReset;

    public string Title { get; }

    /// <summary>Footer note: what OK does to lights that are not selected (and where new ones go).</summary>
    public string RemovalNote { get; }

    /// <summary>The lights matching <see cref="SearchText"/>, in patch order.</summary>
    public ObservableCollection<FixturePickerLight> VisibleLights { get; } = [];

    public ObservableCollection<FixturePickerGroup> Groups { get; } = [];

    public bool HasNoLights => _lights.Count == 0;

    public bool HasNoGroups => Groups.Count == 0;

    /// <summary>There are lights but the search hides all of them.</summary>
    public bool HasNoMatches => _lights.Count > 0 && VisibleLights.Count == 0;

    /// <summary>"_Lights (24)": the underscore makes Alt+L the access key.</summary>
    public string LightsHeader => $"_Lights ({_lights.Count})";

    /// <summary>"_Groups (3)": the underscore makes Alt+G the access key.</summary>
    public string GroupsHeader => $"_Groups ({Groups.Count})";

    public int PickedCount => _picked.Count;

    /// <summary>"5 of 24 lights selected".</summary>
    public string PickedText => $"{_picked.Count} of {_lights.Count} {(_lights.Count == 1 ? "light" : "lights")} selected";

    /// <summary>"2 to add, 1 to remove"; empty when nothing changes.</summary>
    public string ChangeSummary
    {
        get
        {
            var added = 0;
            var removed = 0;
            foreach (var light in _lights)
            {
                if (light.IsPicked && !light.WasUsed)
                {
                    added++;
                }
                else if (!light.IsPicked && light.WasUsed)
                {
                    removed++;
                }
            }

            return (added, removed) switch
            {
                (0, 0) => "No changes",
                (_, 0) => $"{added} to add",
                (0, _) => $"{removed} to remove",
                _ => $"{added} to add, {removed} to remove",
            };
        }
    }

    /// <summary>The function's new fixture list (valid after OK).</summary>
    public IReadOnlyList<Guid> Result => FixtureSelection.Merge(_existing, _patchOrder, _picked);

    /// <summary>The picked lights in patch order.</summary>
    public IReadOnlyList<Guid> PickedIds => _patchOrder.Where(_picked.Contains).ToList();

    /// <summary>
    /// Builds the dialog for a project: every patched light (patch order) and every group. Lights in
    /// <paramref name="existing"/> start picked.
    /// </summary>
    public static FixturePickerViewModel FromProject(Project project, IEnumerable<Guid> existing, string title, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(project);
        var used = existing.ToList();
        var usedSet = used.ToHashSet();
        var lights = new List<FixturePickerLight>(project.Fixtures.Count);
        for (var i = 0; i < project.Fixtures.Count; i++)
        {
            var fixture = project.Fixtures[i];
            var profile = project.FindProfile(fixture.ProfileId);
            var channels = project.ResolveFixtureChannels(fixture).Count;
            var address = channels <= 1
                ? $"U{fixture.Universe} · {fixture.StartAddress}"
                : $"U{fixture.Universe} · {fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, channels)}";
            lights.Add(new FixturePickerLight(
                fixture.Id,
                i + 1,
                fixture.Name,
                address,
                profile is null ? "(missing profile)" : profile.DisplayName,
                fixture.ModeName,
                usedSet.Contains(fixture.Id)));
        }

        var groups = project.Groups.Select(g => (g.Id, g.Name, (IReadOnlyList<Guid>)g.FixtureIds));
        return new FixturePickerViewModel(title, ordered, lights, groups, used);
    }

    /// <summary>
    /// The list selection changed (click, Shift+click, Ctrl+click, Ctrl+A). Called by the view with the data items of
    /// <c>SelectionChanged</c>; ignored while <see cref="VisibleLights"/> is being rebuilt (that clears the list
    /// selection without the user unpicking anything).
    /// </summary>
    public void ApplyListSelection(IList added, IList removed)
    {
        if (_rebuildingList)
        {
            return;
        }

        var changed = false;
        foreach (var item in removed)
        {
            if (item is FixturePickerLight light)
            {
                changed |= SetPicked(light, false);
            }
        }

        foreach (var item in added)
        {
            if (item is FixturePickerLight light)
            {
                changed |= SetPicked(light, true);
            }
        }

        if (changed)
        {
            AfterPickedChanged(resetView: false);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    /// <summary>Picks every light shown (all of them without a search).</summary>
    [RelayCommand]
    private void SelectAll() => SetVisiblePicked(true);

    /// <summary>Unpicks every light shown (all of them without a search).</summary>
    [RelayCommand]
    private void Clear() => SetVisiblePicked(false);

    private void SetVisiblePicked(bool picked)
    {
        var changed = false;
        foreach (var light in VisibleLights)
        {
            changed |= SetPicked(light, picked);
        }

        if (changed)
        {
            AfterPickedChanged(resetView: true);
        }
    }

    private void OnGroupToggled(FixturePickerGroup group, bool picked)
    {
        foreach (var id in group.MemberIds)
        {
            if (_byId.TryGetValue(id, out var light))
            {
                SetPicked(light, picked);
            }
        }

        AfterPickedChanged(resetView: true);
    }

    private bool SetPicked(FixturePickerLight light, bool picked)
    {
        if (picked ? !_picked.Add(light.Id) : !_picked.Remove(light.Id))
        {
            return false;
        }

        light.IsPicked = picked;
        return true;
    }

    private void AfterPickedChanged(bool resetView)
    {
        RefreshGroupStates();
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(PickedText));
        OnPropertyChanged(nameof(ChangeSummary));
        if (resetView)
        {
            SelectionReset?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshGroupStates()
    {
        foreach (var group in Groups)
        {
            group.SetStateSilently(FixtureSelection.GroupState(group.MemberIds, _picked));
        }
    }

    private void ApplySearch()
    {
        var text = SearchText?.Trim() ?? string.Empty;
        _rebuildingList = true;
        try
        {
            VisibleLights.Clear();
            foreach (var light in _lights)
            {
                if (text.Length == 0 || light.SearchKey.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                {
                    VisibleLights.Add(light);
                }
            }
        }
        finally
        {
            _rebuildingList = false;
        }

        OnPropertyChanged(nameof(HasNoMatches));
        SelectionReset?.Invoke(this, EventArgs.Empty);
    }
}
