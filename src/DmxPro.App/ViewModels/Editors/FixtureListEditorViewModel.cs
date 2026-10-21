using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// The "Fixtures" panel of the effect and sequence editors: the fixtures the function drives, in order (order matters
/// for per-fixture offsets). "Add / choose lights…" opens the modal <see cref="FixturePickerViewModel"/> dialog (single
/// lights with Shift/Ctrl multi-select, or whole groups); remove and move up/down work on the selected fixture. Every
/// change is reported through the callback with the fixture ids that were added and removed (both empty for a reorder).
/// </summary>
public sealed partial class FixtureListEditorViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action<IReadOnlyList<Guid>, IReadOnlyList<Guid>> _onChanged;
    private readonly List<Guid> _ids = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private FixtureListItem? _selectedItem;

    public FixtureListEditorViewModel(Project project, Action<IReadOnlyList<Guid>, IReadOnlyList<Guid>> onChanged)
    {
        _project = project;
        _onChanged = onChanged;
        RefreshPatch();
    }

    /// <summary>
    /// Opens the "Choose lights" dialog modally; returns true on OK. Set by the view (the editors are created without
    /// DI), or by tests. While null, <see cref="ChooseLightsCommand"/> does nothing.
    /// </summary>
    public Func<FixturePickerViewModel, bool>? ShowPicker { get; set; }

    /// <summary>Rebuilds the rows after the patch changed (renamed, moved or deleted fixtures).</summary>
    public void RefreshPatch() => Rebuild(SelectedItem?.Id);

    /// <summary>The fixtures in the function, in order.</summary>
    public ObservableCollection<FixtureListItem> Items { get; } = [];

    public bool HasNoItems => Items.Count == 0;

    /// <summary>The fixture ids in order.</summary>
    public IReadOnlyList<Guid> FixtureIds => _ids;

    /// <summary>"3 fixtures".</summary>
    public string CountText => Items.Count == 1 ? "1 fixture" : $"{Items.Count} fixtures";

    /// <summary>
    /// Always null: the owner decided (2026-10) to show no "unverified profile" warnings. Kept because the effect and
    /// sequence editors still copy it into <see cref="FunctionEditorViewModel.UnverifiedWarning"/>.
    /// </summary>
    public string? UnverifiedWarning => null;

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

    /// <summary>
    /// Opens the "Choose lights" dialog with the function's lights picked; on OK the list becomes the picked lights:
    /// existing order kept, unpicked ones removed, new ones appended in patch order.
    /// </summary>
    [RelayCommand]
    private void ChooseLights()
    {
        if (ShowPicker is not { } show)
        {
            return;
        }

        var picker = FixturePickerViewModel.FromProject(_project, _ids, "Choose lights", ordered: true);
        if (!show(picker))
        {
            return;
        }

        var result = picker.Result;
        var added = FixtureSelection.Added(_ids, result);
        var removed = FixtureSelection.Added(result, _ids);
        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        _ids.Clear();
        _ids.AddRange(result);
        Rebuild(SelectedItem?.Id);
        _onChanged(added, removed);
    }

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

    private void Rebuild(Guid? select)
    {
        Items.Clear();
        var fixtures = FixturesById();
        for (var i = 0; i < _ids.Count; i++)
        {
            var id = _ids[i];
            if (fixtures.TryGetValue(id, out var fixture))
            {
                Items.Add(new FixtureListItem(id, i + 1, fixture.Name, Detail(fixture), false, false));
            }
            else
            {
                Items.Add(new FixtureListItem(id, i + 1, "(missing fixture)", "Deleted from the patch — remove it here", false, true));
            }
        }

        SelectedItem = select is { } keep ? Items.FirstOrDefault(i => i.Id == keep) : null;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(CountText));
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
