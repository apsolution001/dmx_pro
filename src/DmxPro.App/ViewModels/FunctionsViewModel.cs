using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Functions page (QLC+ Function Manager layout): function tree grouped by type on top, summary or editor below.
/// Select ≠ edit ≠ preview (ui-plan §3.3): a click only selects and shows an on-screen summary; double-click / Enter /
/// Edit opens the editor of the function's type. While the editor is open on this page (and live mode is off) its
/// unsaved draft is shown on the lights automatically (<see cref="FunctionPreviewService"/>); selecting never changes them.
/// </summary>
public sealed partial class FunctionsViewModel : PageViewModel
{
    private static FunctionTypeOption AllTypes { get; } = new(null, "All types");

    private readonly ProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsService _settings;
    private readonly LightClipboardService _clipboard;
    private readonly VirtualConsoleViewModel _virtualConsole;

    /// <summary>Edit ▸ Cut / Copy / Paste of whole functions (in-app, cleared when another project is opened).</summary>
    private readonly FunctionClipboard _functionClipboard = new();

    private readonly FunctionPreviewService _preview;

    /// <summary>True while the page is shown: only then does the open editor preview on the lights.</summary>
    private bool _isPageVisible;

    /// <summary>True while the list rebuilds or a selection is put back, so selection changes don't prompt.</summary>
    private bool _suppressSelection;

    /// <summary><see cref="ProjectService.Revision"/> the lists were last rebuilt from (-1 = never).</summary>
    private long _loadedRevision = -1;

    /// <summary>
    /// Rows built for <see cref="_rowCacheRevision"/>. Typing in the search box or changing the type filter reuses them
    /// instead of rebuilding every row (and its colour swatch) per keystroke; any project change starts a new cache.
    /// </summary>
    private readonly Dictionary<LightFunction, FunctionRowViewModel> _rowCache = [];

    private long _rowCacheRevision = -1;

    /// <summary>Type folders the user collapsed; remembered while a search opens every folder.</summary>
    private readonly HashSet<FunctionType> _collapsedTypes = [];

    /// <summary>The current folders were built for a search (all open), so their expanded state is not the user's.</summary>
    private bool _groupsBuiltForSearch;

    /// <summary>The selected function while the search/type filter hides it; selected again once it is listed.</summary>
    private LightFunction? _selectionHiddenByFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedScene), nameof(SelectedIsShowCopy), nameof(SelectedShowInList))]
    [NotifyCanExecuteChangedFor(
        nameof(EditCommand),
        nameof(DuplicateCommand),
        nameof(DeleteCommand),
        nameof(BeginRenameCommand),
        nameof(CopyCommand),
        nameof(CutCommand),
        nameof(AddToVirtualConsoleCommand))]
    private FunctionRowViewModel? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsSummaryVisible))]
    private FunctionEditorViewModel? _editor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private string _search = string.Empty;

    [ObservableProperty]
    private FunctionTypeOption _typeFilter = AllTypes;

    [ObservableProperty]
    private bool _showHiddenShowCopies;

    public FunctionsViewModel(
        ProjectService projects,
        IDialogService dialogs,
        AppSettingsService settings,
        LightClipboardService clipboard,
        VirtualConsoleViewModel virtualConsole,
        FunctionPreviewService preview)
    {
        _projects = projects;
        _dialogs = dialogs;
        _settings = settings;
        _clipboard = clipboard;
        _virtualConsole = virtualConsole;
        _functionClipboard.Changed += (_, _) => PasteCommand.NotifyCanExecuteChanged();
        _preview = preview;
        TypeOptions = [AllTypes, .. Enum.GetValues<FunctionType>().Select(t => new FunctionTypeOption(t, FunctionRowViewModel.TypeDisplayName(t)))];

        // A different project replaces everything; the shell has already asked about saving the project.
        _projects.ProjectChanged += (_, _) =>
        {
            Editor = null;

            // Copied functions point at this project's lights and functions by id: never paste them into another one.
            _functionClipboard.Clear();
            Reload();
        };

        // Lights added/removed/renamed while an editor is open must show up in its Add list.
        _projects.ContentChanged += (_, _) => Editor?.RefreshPatch();
        Reload();
    }

    /// <summary>
    /// Short success notes for the shell's message bar ("Copied …", "Added … to the Virtual Console"). Failures use
    /// dialogs instead.
    /// </summary>
    public event EventHandler<string>? StatusMessage;

    public override string Title => "Functions";

    public override string Subtitle => "Scenes, chasers and effects. Click to select, double-click to edit.";

    /// <summary>Every visible function in tree order (type, then name). Keyboard/delete logic works on this list.</summary>
    public ObservableCollection<FunctionRowViewModel> Rows { get; } = [];

    /// <summary>The tree: one folder per function type that has visible functions.</summary>
    public ObservableCollection<FunctionTreeGroupViewModel> Groups { get; } = [];

    public IReadOnlyList<FunctionTypeOption> TypeOptions { get; }

    /// <summary>On-screen preview of the selected function (summary panel).</summary>
    public ObservableCollection<PreviewLight> SelectedPreviewLights { get; } = [];

    public bool HasSelection => SelectedRow is not null;

    public SceneFunction? SelectedScene => SelectedRow?.Function as SceneFunction;

    public bool IsEditing => Editor is not null;

    public bool IsSummaryVisible => Editor is null && SelectedRow is not null;

    public bool IsNothingSelected => Editor is null && SelectedRow is null;

    public bool SelectedIsShowCopy => SelectedRow?.Function.OwnerShowId is not null;

    /// <summary>"Show in function list" of the selected show copy, changed directly from the summary panel.</summary>
    public bool SelectedShowInList
    {
        get => SelectedRow?.Function.ShowInFunctionList ?? false;
        set
        {
            if (SelectedRow is not { } row || row.Function.OwnerShowId is null || row.Function.ShowInFunctionList == value)
            {
                return;
            }

            _projects.Current.SetShowInFunctionList(row.Function.Id, value);
            _projects.MarkChanged(affectsPlayback: false);
            row.Refresh(_projects.Current);
            OnPropertyChanged();
            if (!value && !ShowHiddenShowCopies)
            {
                Reload();
            }
        }
    }

    /// <summary>The project has no listed functions at all: the list shows the "No functions yet" empty state.</summary>
    public bool IsListEmpty => !_projects.Current.FunctionListItems.Any() && !(ShowHiddenShowCopies && _projects.Current.Functions.Count > 0);

    /// <summary>Functions exist but the search/type filter hides all of them.</summary>
    public bool NoFilterResults => !IsListEmpty && Rows.Count == 0;

    public override void OnNavigatedTo()
    {
        _isPageVisible = true;

        // Rebuilding the tree on every visit made switching to this page slow; only rebuild when the project changed.
        if (_loadedRevision != _projects.Revision)
        {
            Reload();
        }

        Editor?.RefreshPatch();
        if (Editor is { } editor)
        {
            _preview.Attach(editor);
        }
    }

    /// <summary>Leaving the page stops the editor's light preview; it comes back when the page is shown again.</summary>
    public override void OnNavigatedFrom()
    {
        _isPageVisible = false;
        if (Editor is { } editor)
        {
            _preview.Detach(editor);
        }
    }

    /// <summary>
    /// Closes the editor, asking Save / Don't save / Cancel when it has unsaved changes. Returns false when the user
    /// cancelled (the editor stays open). The shell can call this before leaving the page or replacing the project.
    /// </summary>
    public async Task<bool> ConfirmCloseEditorAsync()
    {
        if (Editor is not { } editor)
        {
            return true;
        }

        if (editor.IsDirty)
        {
            switch (await _dialogs.AskSaveChangesAsync(editor.Original.Name))
            {
                case SaveChangesChoice.Cancel:
                    return false;
                case SaveChangesChoice.Save:
                    editor.Save();
                    break;
            }
        }

        CloseEditorSilently();
        return true;
    }

    partial void OnSelectedRowChanged(FunctionRowViewModel? oldValue, FunctionRowViewModel? newValue)
    {
        OnPropertyChanged(nameof(IsSummaryVisible));
        OnPropertyChanged(nameof(IsNothingSelected));
        SyncTreeSelection(oldValue, newValue);
        UpdateSelectedPreview();
        if (!_suppressSelection)
        {
            // The user picked something else (or a folder): forget a selection the filter was hiding.
            _selectionHiddenByFilter = null;
        }

        if (_suppressSelection || Editor is null || ReferenceEquals(Editor.Original, newValue?.Function))
        {
            return;
        }

        _ = LeaveEditorForSelectionAsync(oldValue);
    }

    partial void OnEditorChanged(FunctionEditorViewModel? oldValue, FunctionEditorViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Saved -= OnEditorSaved;
            oldValue.CloseRequested -= OnEditorCloseRequested;
            _preview.Detach(oldValue);
            oldValue.Detach();
        }

        if (newValue is not null)
        {
            newValue.Saved += OnEditorSaved;
            newValue.CloseRequested += OnEditorCloseRequested;
            if (_isPageVisible)
            {
                _preview.Attach(newValue);
            }
        }

        OnPropertyChanged(nameof(IsNothingSelected));
    }

    partial void OnSearchChanged(string value) => Reload();

    /// <summary>Esc in the search box: clears it (when it is already empty, Esc falls through to the page).</summary>
    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch() => Search = string.Empty;

    private bool CanClearSearch() => Search.Length > 0;

    partial void OnTypeFilterChanged(FunctionTypeOption value)
    {
        // The ComboBox pushes null when its selected item disappears.
        if (value is null)
        {
            TypeFilter = AllTypes;
            return;
        }

        Reload();
    }

    partial void OnShowHiddenShowCopiesChanged(bool value) => Reload();

    // ───────────────────────────── Commands ─────────────────────────────

    /// <summary>Empty-state button: a new scene.</summary>
    [RelayCommand]
    private Task NewSceneAsync() => NewFunctionAsync(FunctionType.Scene);

    /// <summary>"New > Chaser" etc.: creates an empty function of that type and opens its editor.</summary>
    [RelayCommand]
    private async Task NewFunctionAsync(FunctionType type)
    {
        if (!await ConfirmCloseEditorAsync())
        {
            return;
        }

        var function = _projects.Current.CreateFunction(type);
        _projects.MarkChanged();
        ClearFilters();
        Reload();
        Editor = FunctionEditorFactory.Create(_projects.Current, function, _settings, _clipboard);
        SelectSilently(function);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync()
    {
        if (SelectedRow?.Function is not { } function || ReferenceEquals(Editor?.Original, function))
        {
            return;
        }

        if (!await ConfirmCloseEditorAsync())
        {
            return;
        }

        Editor = FunctionEditorFactory.Create(_projects.Current, function, _settings, _clipboard);
    }

    [RelayCommand]
    private async Task CloseEditorAsync() => await ConfirmCloseEditorAsync();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var copy = _projects.Current.DuplicateFunction(row.Function.Id);
        _projects.MarkChanged();
        Reload();
        SelectedRow = Rows.FirstOrDefault(r => r.Function == copy) ?? SelectedRow;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var project = _projects.Current;
        var function = row.Function;
        var usedByShow = function.OwnerShowId is not null && project.IsUsedByShow(function.Id);
        var referencedBy = ReferencesText(project, function);
        var confirmed = usedByShow
            ? await _dialogs.ConfirmAsync(
                "Remove from list",
                $"\"{function.Name}\" is still used in show \"{project.FindShow(function.OwnerShowId!.Value)?.Name}\". It will be removed from the Functions list but stays in the show.",
                "Remove from list")
            : await _dialogs.ConfirmAsync(
                "Delete function",
                referencedBy is null
                    ? $"Delete \"{function.Name}\"? Shows that use it keep their own copy."
                    : $"Delete \"{function.Name}\"? It is used by:{Environment.NewLine}{referencedBy}{Environment.NewLine}{Environment.NewLine}"
                      + "Those steps and fades will play nothing until you pick another function. Shows keep their own copy.",
                "Delete");
        if (!confirmed)
        {
            return;
        }

        if (ReferenceEquals(Editor?.Original, function))
        {
            CloseEditorSilently();
        }

        project.DeleteFunction(function.Id);
        _projects.MarkChanged();
        var index = Rows.IndexOf(row);
        Reload();
        if (Rows.Count > 0)
        {
            SelectedRow = Rows[Math.Clamp(index, 0, Rows.Count - 1)];
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BeginRename() => SelectedRow?.BeginRename();

    private bool CanEdit() => SelectedRow is not null;

    /// <summary>Edit ▸ Copy / Ctrl+C: puts a snapshot of the selected function on the function clipboard.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (SelectedRow?.Function is not { } function)
        {
            return;
        }

        _functionClipboard.Copy(function);
        StatusMessage?.Invoke(this, $"Copied \"{function.Name}\". Paste (Ctrl+V) adds a copy to the Functions list.");
    }

    /// <summary>
    /// Edit ▸ Cut / Ctrl+X: Delete (with its usual confirmation); only when the function really left the list is it put
    /// on the clipboard, so cancelling the confirmation keeps the clipboard as it was.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CutAsync()
    {
        if (SelectedRow?.Function is not { } function)
        {
            return;
        }

        await DeleteAsync();
        if (_projects.Current.FunctionListItems.Contains(function))
        {
            return;
        }

        _functionClipboard.Copy(function);
        StatusMessage?.Invoke(this, $"Cut \"{function.Name}\". Paste (Ctrl+V) adds it back as a copy.");
    }

    /// <summary>Edit ▸ Paste / Ctrl+V: adds a copy of the clipboard function to the library and selects it.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_functionClipboard.PasteInto(_projects.Current) is not { } copy)
        {
            return;
        }

        _projects.MarkChanged();
        Reload();
        if (!Rows.Any(r => r.Function == copy))
        {
            // The search or type filter would hide the pasted function.
            ClearFilters();
        }

        SelectedRow = Rows.FirstOrDefault(r => r.Function == copy) ?? SelectedRow;
        StatusMessage?.Invoke(this, $"Pasted \"{copy.Name}\".");
    }

    private bool CanPaste() => _functionClipboard.HasFunction;

    /// <summary>
    /// Context menu "Add to Virtual Console": a toggle button for the selected function on the current console page
    /// (a new page when there is none yet). The Functions page stays open.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AddToVirtualConsoleAsync()
    {
        if (SelectedRow?.Function is not { } function)
        {
            return;
        }

        if (_virtualConsole.AddFunctionButton(function.Id) is { } pageName)
        {
            StatusMessage?.Invoke(this, $"Added a button for \"{function.Name}\" to Virtual Console page \"{pageName}\" (Ctrl+4).");
            return;
        }

        await _dialogs.ShowInfoAsync(
            "Console page is full",
            "There is no free space for another button on the current Virtual Console page. Make room on that page, or select another page in the Virtual Console, then try again.");
    }

    /// <summary>Bullet list of the chasers / fades (library or show copies) that play the function; null when none.</summary>
    private static string? ReferencesText(Project project, LightFunction function)
    {
        var users = project.FunctionsReferencing(function.Id);
        if (users.Count == 0)
        {
            return null;
        }

        var lines = users.Select(u =>
        {
            var owner = u.OwnerShowId is { } showId ? $" (in show \"{project.FindShow(showId)?.Name ?? "deleted show"}\")" : string.Empty;
            return $"  • {FunctionRowViewModel.TypeDisplayName(u.Type)} \"{u.Name}\"{owner}";
        });
        return string.Join(Environment.NewLine, lines);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private async Task LeaveEditorForSelectionAsync(FunctionRowViewModel? previous)
    {
        if (await ConfirmCloseEditorAsync())
        {
            return;
        }

        // Cancelled: go back to the function being edited.
        _suppressSelection = true;
        SelectedRow = previous;
        _suppressSelection = false;
    }

    /// <summary>Mirrors <see cref="SelectedRow"/> into the tree (expanding its folder so the item exists).</summary>
    private void SyncTreeSelection(FunctionRowViewModel? oldValue, FunctionRowViewModel? newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue))
        {
            oldValue.IsSelected = false;
        }

        if (newValue is null)
        {
            return;
        }

        if (Groups.FirstOrDefault(g => g.Children.Contains(newValue)) is { IsExpanded: false } folder)
        {
            folder.IsExpanded = true;
        }

        newValue.IsSelected = true;
    }

    private void OnRowSelectedInTree(FunctionRowViewModel row)
    {
        if (!ReferenceEquals(SelectedRow, row) && Rows.Contains(row))
        {
            SelectedRow = row;
        }
    }

    /// <summary>A type folder was selected: no function is selected any more (the editor asks about unsaved changes).</summary>
    private void OnGroupSelectedInTree(FunctionTreeGroupViewModel group)
    {
        if (!_suppressSelection && Groups.Contains(group))
        {
            // SelectedRow may already be null while a search hides the selection: forget that one explicitly too.
            _selectionHiddenByFilter = null;
            SelectedRow = null;
        }
    }

    private void CloseEditorSilently()
    {
        Editor = null;
        UpdateSelectedPreview();
    }

    private void OnEditorSaved(object? sender, EventArgs e)
    {
        _projects.MarkChanged();
        Reload();
    }

    /// <summary>The editor's own Close command goes through the same unsaved-changes prompt.</summary>
    private void OnEditorCloseRequested(object? sender, EventArgs e) => _ = ConfirmCloseEditorAsync();

    private bool RenameRow(FunctionRowViewModel row, string name)
    {
        if (!_projects.Current.RenameFunction(row.Function.Id, name))
        {
            return false;
        }

        _projects.MarkChanged(affectsPlayback: false);
        if (Editor is { } editor && ReferenceEquals(editor.Original, row.Function))
        {
            editor.SyncName(row.Function.Name);
        }

        return true;
    }

    /// <summary>Makes sure a newly created function is visible in the list.</summary>
    private void ClearFilters()
    {
        Search = string.Empty;
        TypeFilter = AllTypes;
    }

    private void SelectSilently(LightFunction function)
    {
        _suppressSelection = true;
        SelectedRow = Rows.FirstOrDefault(r => r.Function == function);
        _suppressSelection = false;
    }

    /// <summary>Rebuilds the list from the project and the filters, keeping the selection when possible.</summary>
    private void Reload()
    {
        _loadedRevision = _projects.Revision;
        var project = _projects.Current;
        var selected = SelectedRow?.Function ?? _selectionHiddenByFilter;
        var terms = Search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var searching = terms.Length > 0;
        IEnumerable<LightFunction> source = ShowHiddenShowCopies ? project.Functions : project.FunctionListItems;

        if (_rowCacheRevision != _projects.Revision)
        {
            _rowCache.Clear();
            _rowCacheRevision = _projects.Revision;
        }

        // Folders the user collapsed stay collapsed across rebuilds; a search opens them all so no match is hidden,
        // and clearing the search brings the user's folders back.
        if (!_groupsBuiltForSearch)
        {
            foreach (var group in Groups)
            {
                if (group.IsExpanded)
                {
                    _collapsedTypes.Remove(group.Type);
                }
                else
                {
                    _collapsedTypes.Add(group.Type);
                }
            }
        }

        _groupsBuiltForSearch = searching;

        _suppressSelection = true;
        Rows.Clear();
        Groups.Clear();
        var rows = new List<FunctionRowViewModel>();
        foreach (var function in source)
        {
            if (TypeFilter.Type is { } type && function.Type != type)
            {
                continue;
            }

            if (searching && !MatchesSearch(function, project, terms))
            {
                continue;
            }

            if (!_rowCache.TryGetValue(function, out var row))
            {
                row = new FunctionRowViewModel(function, RenameRow, OnRowSelectedInTree);
                row.Refresh(project);
                _rowCache[function] = row;
            }

            rows.Add(row);
        }

        foreach (var byType in rows.GroupBy(r => r.Function.Type).OrderBy(g => g.Key))
        {
            var children = byType.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var child in children)
            {
                Rows.Add(child);
            }

            Groups.Add(new FunctionTreeGroupViewModel(byType.Key, children, OnGroupSelectedInTree)
            {
                IsExpanded = searching || !_collapsedTypes.Contains(byType.Key) || children.Any(c => c.Function == selected),
            });
        }

        SelectedRow = Rows.FirstOrDefault(r => r.Function == selected);
        _selectionHiddenByFilter = SelectedRow is null && selected is not null && project.Functions.Contains(selected) ? selected : null;
        _suppressSelection = false;

        // The edited function was deleted, hidden or the project replaced: the editor has nothing left to edit.
        if (Editor is not null && !project.Functions.Contains(Editor.Original))
        {
            Editor = null;
        }

        UpdateSelectedPreview();
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(NoFilterResults));
        OnPropertyChanged(nameof(SelectedShowInList));
    }

    /// <summary>
    /// Every search word must appear (any order, any case) in the function's name, its type or the name of the show that
    /// owns it, so "red wash" finds "Wash red front" and "wedding" finds that show's copies.
    /// </summary>
    private static bool MatchesSearch(LightFunction function, Project project, string[] terms)
    {
        var typeName = FunctionRowViewModel.TypeDisplayName(function.Type);
        var showName = function.OwnerShowId is { } showId ? project.FindShow(showId)?.Name : null;
        foreach (var term in terms)
        {
            if (!function.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                && !typeName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                && showName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) != true)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateSelectedPreview()
    {
        SelectedPreviewLights.Clear();
        var project = _projects.Current;

        // Lookup tables: a big scene or effect covers hundreds of lights; per-light project/scene scans were quadratic.
        var fixtures = new Dictionary<Guid, Fixture>(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            fixtures.TryAdd(fixture.Id, fixture);
        }

        if (SelectedScene is { } scene)
        {
            var values = scene.IndexValues();
            foreach (var fixtureId in scene.TargetFixtureIds())
            {
                if (fixtures.TryGetValue(fixtureId, out var fixture))
                {
                    SelectedPreviewLights.Add(new PreviewLight(fixture.Name, PreviewLight.ToHex(values.PreviewColor(project, fixture))));
                }
            }

            return;
        }

        // Effects: each fixture in the effect's (first) colour; a dimmer-only pulse shows warm white.
        if (SelectedRow?.Function is FixtureEffectFunction effect)
        {
            var color = ShowsLibraryItem.PreviewColor(effect, project) ?? new RgbColor(255, 236, 200);
            foreach (var fixtureId in effect.FixtureIds)
            {
                if (fixtures.TryGetValue(fixtureId, out var fixture))
                {
                    SelectedPreviewLights.Add(new PreviewLight(fixture.Name, color.ToHex()));
                }
            }
        }
    }
}
