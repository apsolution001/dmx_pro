using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Virtual Console page: page tabs, the widget canvas (operate or edit layout), the widget palette and properties.
/// Widgets operate through <see cref="IConsolePlayback"/>; layout edits go through the project API and mark it dirty.
/// Operator mode is session-only (never saved) and hides all editing chrome.
/// </summary>
public sealed partial class VirtualConsoleViewModel : PageViewModel
{
    /// <summary>Smallest cell size in pixels: a default 2×2 widget is then 80 px, above the 64 px live-target minimum.</summary>
    public const double MinCellSize = 40;

    private readonly ProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly SynchronizationContext? _uiContext;
    private readonly HashSet<ConsoleWidgetViewModel> _heldFlashes = [];
    private readonly Dictionary<Guid, string> _functionNames = [];
    private readonly Dictionary<Guid, string> _showNames = [];
    private readonly Dictionary<Guid, string> _groupNames = [];
    private bool _layoutChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPage), nameof(CanvasWidth), nameof(CanvasHeight), nameof(IsSelectedPageEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RenamePageCommand), nameof(DeletePageCommand), nameof(AddWidgetCommand))]
    private VirtualConsolePageViewModel? _selectedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOperatingMode))]
    private bool _isEditMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEditChrome))]
    private bool _isOperatorMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWidget), nameof(HasNoSelectedWidget), nameof(SelectedTargetOptions))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWidgetCommand), nameof(DuplicateWidgetCommand), nameof(SetColorCommand))]
    private ConsoleWidgetViewModel? _selectedWidget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasWidth), nameof(CanvasHeight))]
    private double _cellSize = MinCellSize;

    /// <summary>The fixed "Shows" tab (list of every show) is open instead of a console page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCanvas), nameof(ShowNoConsoleState))]
    private bool _isShowsTabSelected;

    public VirtualConsoleViewModel(ProjectService projects, IConsolePlayback playback, IDialogService dialogs, AppSettingsService settings, IShowMusicService music)
    {
        _projects = projects;
        _dialogs = dialogs;
        Playback = playback;
        _uiContext = SynchronizationContext.Current;
        ShowPlayer = new ShowPlayerViewModel(projects, playback, music) { LargeStepSeconds = settings.Current.SeekStepSeconds };
        settings.Changed += (_, _) => ShowPlayer.LargeStepSeconds = settings.Current.SeekStepSeconds;
        _projects.ProjectChanged += (_, _) => Reload();
        Playback.StateChanged += OnPlaybackStateChanged;
        Reload();
    }

    public override string Title => "Virtual Console";

    public override string Subtitle => "Your live control surface: buttons, sliders and pages.";

    public IConsolePlayback Playback { get; }

    /// <summary>The Shows tab list and the transport bar at the bottom of the console.</summary>
    public ShowPlayerViewModel ShowPlayer { get; }

    public ObservableCollection<VirtualConsolePageViewModel> Pages { get; } = [];

    public IReadOnlyList<VirtualConsolePaletteItem> Palette { get; } =
    [
        new(VirtualConsoleWidgetKind.FunctionButton, "Function button", "Scenes", "Switches a function on/off"),
        new(VirtualConsoleWidgetKind.FlashButton, "Flash button", "Flash", "Runs a function only while held"),
        new(VirtualConsoleWidgetKind.ShowButton, "Show button", "Play", "Starts/stops a show"),
        new(VirtualConsoleWidgetKind.GroupLevel, "Group level", "Group", "Fader for a fixture group"),
        new(VirtualConsoleWidgetKind.Master, "Master", "Dimmer", "Grand master fader"),
        new(VirtualConsoleWidgetKind.Blackout, "Blackout", "Blackout", "Blackout on/off"),
        new(VirtualConsoleWidgetKind.ReleaseAll, "Release All", "ReleaseAll", "Stops all functions and shows"),
        new(VirtualConsoleWidgetKind.Speed, "Speed", "Pulse", "Speed fader for one or all functions"),
        new(VirtualConsoleWidgetKind.Label, "Label", "Rename", "Text heading on the page"),
    ];

    public IReadOnlyList<VirtualConsoleColorPreset> ColorPresets { get; } =
    [
        new("Red", "#FF0000"),
        new("Orange", "#FF8000"),
        new("Amber", "#FFBF00"),
        new("Yellow", "#FFFF00"),
        new("Green", "#00FF00"),
        new("Cyan", "#00FFFF"),
        new("Blue", "#0000FF"),
        new("UV / Violet", "#8000FF"),
        new("Magenta", "#FF00FF"),
        new("Pink", "#FF0080"),
        new("White", "#FFFFFF"),
        new("Warm white", "#FFD8A0"),
    ];

    public IReadOnlyList<FunctionWidgetMode> ModeOptions { get; } = [FunctionWidgetMode.Toggle, FunctionWidgetMode.Flash];

    public ObservableCollection<VirtualConsoleTargetOption> FunctionOptions { get; } = [];

    public ObservableCollection<VirtualConsoleTargetOption> ShowOptions { get; } = [];

    public ObservableCollection<VirtualConsoleTargetOption> GroupOptions { get; } = [];

    /// <summary>Speed targets: "All functions" plus every function.</summary>
    public ObservableCollection<VirtualConsoleTargetOption> SpeedOptions { get; } = [];

    /// <summary>Choices for the selected widget's target picker.</summary>
    public IEnumerable<VirtualConsoleTargetOption> SelectedTargetOptions => SelectedWidget?.Model switch
    {
        ShowWidget => ShowOptions,
        GroupLevelWidget => GroupOptions,
        SpeedWidget => SpeedOptions,
        FunctionWidget => FunctionOptions,
        _ => [],
    };

    public bool HasPages => Pages.Count > 0;

    /// <summary>No console pages yet: the "No console yet" empty state is shown.</summary>
    public bool IsEmpty => Pages.Count == 0;

    public bool HasSelectedPage => SelectedPage is not null;

    public bool HasNoSelectedWidget => SelectedWidget is null;

    public bool HasSelectedWidget => SelectedWidget is not null;

    /// <summary>A page exists but has no widgets: the canvas shows a hint.</summary>
    public bool IsSelectedPageEmpty => SelectedPage is { Widgets.Count: 0 };

    public bool IsOperatingMode => !IsEditMode;

    /// <summary>The widget canvas is visible: a page exists and the Shows tab is not open.</summary>
    public bool ShowCanvas => HasPages && !IsShowsTabSelected;

    /// <summary>"No console yet" empty state: no pages, and the Shows tab is not open.</summary>
    public bool ShowNoConsoleState => IsEmpty && !IsShowsTabSelected;

    /// <summary>False in operator mode: page add/rename/delete and the Edit layout toggle are hidden.</summary>
    public bool ShowEditChrome => !IsOperatorMode;

    public double CanvasWidth => (SelectedPage?.Columns ?? ConsolePage.DefaultColumns) * CellSize;

    public double CanvasHeight => (SelectedPage?.Rows ?? ConsolePage.DefaultRows) * CellSize;

    public static string DefaultLabel(VirtualConsoleWidgetKind kind) => kind switch
    {
        VirtualConsoleWidgetKind.FunctionButton => "Function",
        VirtualConsoleWidgetKind.FlashButton => "Flash",
        VirtualConsoleWidgetKind.ShowButton => "Show",
        VirtualConsoleWidgetKind.GroupLevel => "Group",
        VirtualConsoleWidgetKind.Master => "Master",
        VirtualConsoleWidgetKind.Blackout => "Blackout",
        VirtualConsoleWidgetKind.ReleaseAll => "Release All",
        VirtualConsoleWidgetKind.Speed => "Speed",
        _ => "Label",
    };

    public override void OnNavigatedTo()
    {
        // Functions, shows or groups may have been renamed or deleted on other pages.
        RebuildTargets();
        RefreshWidgets();
        ShowPlayer.Refresh();
    }

    /// <summary>Leaving the console ends every held flash (safety rule 5); the view stays alive while hidden.</summary>
    public override void OnNavigatedFrom() => ReleaseFlashes();

    // ───────────────────────────── Pages ─────────────────────────────

    [RelayCommand]
    private void CreateDefaultPage()
    {
        var page = _projects.Current.CreateDefaultConsolePage("Main");
        AddPageViewModel(page);
    }

    [RelayCommand]
    private void AddPage()
    {
        var names = _projects.Current.ConsolePages.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = Pages.Count + 1;
        while (names.Contains($"Page {n}"))
        {
            n++;
        }

        AddPageViewModel(_projects.Current.AddConsolePage($"Page {n}"));
    }

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void RenamePage(VirtualConsolePageViewModel? page)
    {
        page ??= SelectedPage;
        if (page is null || IsOperatorMode)
        {
            return;
        }

        page.EditName = page.Name;
        page.IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename(VirtualConsolePageViewModel? page)
    {
        if (page is not { IsRenaming: true })
        {
            return;
        }

        page.IsRenaming = false;
        var name = page.EditName.Trim();
        if (name.Length > 0 && name != page.Name)
        {
            _projects.Current.RenameConsolePage(page.Id, name);
            page.RaiseNameChanged();
            _projects.MarkChanged(affectsPlayback: false);
        }
    }

    [RelayCommand]
    private void CancelRename(VirtualConsolePageViewModel? page)
    {
        page ??= SelectedPage;
        if (page is not null)
        {
            page.IsRenaming = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private async Task DeletePageAsync(VirtualConsolePageViewModel? page)
    {
        page ??= SelectedPage;
        if (page is null || IsOperatorMode)
        {
            return;
        }

        var count = page.Widgets.Count;
        var what = count == 0 ? "It has no widgets." : $"Its {count} widget{(count == 1 ? string.Empty : "s")} will be removed too.";
        if (!await _dialogs.ConfirmAsync("Delete console page", $"Delete page \"{page.Name}\"? {what}", "Delete"))
        {
            return;
        }

        foreach (var widget in page.Widgets)
        {
            widget.ReleaseFlash();
        }

        var index = Pages.IndexOf(page);
        _projects.Current.RemoveConsolePage(page.Id);
        Pages.Remove(page);
        SelectedPage = Pages.Count == 0 ? null : Pages[Math.Clamp(index, 0, Pages.Count - 1)];
        RaisePageCountChanged();
        _projects.MarkChanged(affectsPlayback: false);
    }

    private bool CanEditPage(VirtualConsolePageViewModel? page) => (page ?? SelectedPage) is not null;

    [RelayCommand]
    private void ToggleOperatorMode() => IsOperatorMode = !IsOperatorMode;

    /// <summary>Opens the fixed Shows tab. No console page is selected while it is open.</summary>
    [RelayCommand]
    private void OpenShowsTab()
    {
        IsShowsTabSelected = true;
        IsEditMode = false;
        SelectedPage = null;
        ShowPlayer.Refresh();
    }

    internal void SetButtonsAsSliders(VirtualConsolePageViewModel page, bool value)
    {
        ReleaseFlashes();
        page.Model.ShowButtonsAsSliders = value;
        foreach (var widget in page.Widgets)
        {
            widget.RefreshLayout();
            widget.Refresh();
        }

        _projects.MarkChanged(affectsPlayback: false);
    }

    // ───────────────────────────── Widgets (edit mode) ─────────────────────────────

    /// <summary>Palette click: places the widget at the first free slot of the page.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedPage))]
    private async Task AddWidgetAsync(VirtualConsoleWidgetKind kind)
    {
        if (SelectedPage is not { } page)
        {
            return;
        }

        var widget = CreateWidget(kind);
        if (page.Model.FindFreeSlot(widget.Width, widget.Height) is not { } slot)
        {
            await _dialogs.ShowInfoAsync(
                "Page is full",
                "There is no free space for this widget. Make room, or drag it from the palette onto the page to place it on top.");
            return;
        }

        widget.X = slot.X;
        widget.Y = slot.Y;
        PlaceWidget(page, widget);
    }

    /// <summary>
    /// Functions page "Add to Virtual Console": adds a toggle button for <paramref name="functionId"/> at the first free
    /// spot of the selected console page (the first page while the Shows tab is open; a new page when there is none).
    /// Edit mode and the widget selection stay as they are. Returns the page name, or null when the page is full.
    /// </summary>
    public string? AddFunctionButton(Guid functionId)
    {
        var page = SelectedPage ?? Pages.FirstOrDefault();
        if (page is null)
        {
            AddPage();
            page = SelectedPage!;
        }

        if (_projects.Current.AddFunctionButton(page.Id, functionId) is not { } widget)
        {
            return null;
        }

        // The function may be new since the console last listed targets; without its name the button reads "unassigned".
        RebuildTargets();
        page.Widgets.Add(new ConsoleWidgetViewModel(this, page, widget));
        OnPropertyChanged(nameof(IsSelectedPageEmpty));
        _projects.MarkChanged(affectsPlayback: false);
        return page.Name;
    }

    /// <summary>Palette drag-and-drop: places the widget at a grid cell (clamped to the page).</summary>
    public void AddWidgetAt(VirtualConsoleWidgetKind kind, int x, int y)
    {
        if (SelectedPage is not { } page)
        {
            return;
        }

        var widget = CreateWidget(kind);
        widget.X = x;
        widget.Y = y;
        PlaceWidget(page, widget);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWidget))]
    private async Task DeleteWidgetAsync()
    {
        if (SelectedWidget is not { } widget || !IsEditMode)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync("Delete widget", $"Delete \"{widget.DisplayLabel}\" from this page?", "Delete"))
        {
            return;
        }

        widget.ReleaseFlash();
        _projects.Current.RemoveConsoleWidget(widget.Page.Id, widget.Id);
        widget.Page.Widgets.Remove(widget);
        SelectedWidget = null;
        OnPropertyChanged(nameof(IsSelectedPageEmpty));
        _projects.MarkChanged(affectsPlayback: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWidget))]
    private void DuplicateWidget()
    {
        if (SelectedWidget is not { } widget || !IsEditMode)
        {
            return;
        }

        var page = widget.Page;
        var copy = _projects.Current.DuplicateConsoleWidget(page.Id, widget.Id);
        var vm = new ConsoleWidgetViewModel(this, page, copy);
        page.Widgets.Insert(page.Widgets.IndexOf(widget) + 1, vm);
        SelectWidget(vm);
        _projects.MarkChanged(affectsPlayback: false);
    }

    /// <summary>Preset swatch or "theme colour" (null).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedWidget))]
    private void SetColor(string? hex) => SelectedWidget?.ApplyColorPreset(hex);

    public void SelectWidget(ConsoleWidgetViewModel? widget)
    {
        if (IsEditMode)
        {
            SelectedWidget = widget;
        }
    }

    /// <summary>Drag-move in cells (fractional; the project snaps and clamps). Call <see cref="EndLayoutChange"/> after.</summary>
    public void MoveWidget(ConsoleWidgetViewModel widget, double x, double y)
    {
        if (!IsEditMode)
        {
            return;
        }

        var (oldX, oldY) = (widget.X, widget.Y);
        _projects.Current.MoveConsoleWidget(widget.Page.Id, widget.Id, x, y);
        if (widget.X != oldX || widget.Y != oldY)
        {
            _layoutChanged = true;
            widget.RefreshLayout();
        }
    }

    /// <summary>Drag-resize in cells (fractional; snapped and clamped, min 1×1). Call <see cref="EndLayoutChange"/> after.</summary>
    public void ResizeWidget(ConsoleWidgetViewModel widget, double width, double height)
    {
        if (!IsEditMode)
        {
            return;
        }

        var (oldW, oldH) = (widget.Width, widget.Height);
        _projects.Current.ResizeConsoleWidget(widget.Page.Id, widget.Id, width, height);
        if (widget.Width != oldW || widget.Height != oldH)
        {
            _layoutChanged = true;
            widget.RefreshLayout();
        }
    }

    /// <summary>Ends a drag or keyboard nudge: marks the project dirty once if the layout changed.</summary>
    public void EndLayoutChange()
    {
        if (_layoutChanged)
        {
            _layoutChanged = false;
            _projects.MarkChanged(affectsPlayback: false);
        }
    }

    /// <summary>Arrow keys in edit mode: move the selected widget by cells (resize when <paramref name="resize"/>).</summary>
    public bool NudgeSelected(int dx, int dy, bool resize)
    {
        if (!IsEditMode || SelectedWidget is not { } widget)
        {
            return false;
        }

        if (resize)
        {
            ResizeWidget(widget, widget.Width + dx, widget.Height + dy);
        }
        else
        {
            MoveWidget(widget, widget.X + dx, widget.Y + dy);
        }

        EndLayoutChange();
        return true;
    }

    // ───────────────────────────── Live operation ─────────────────────────────

    /// <summary>Key pressed (normalised name such as "A", "1", "F5"). Returns true when a widget used it.</summary>
    public bool ShortcutDown(string key)
    {
        if (IsEditMode || SelectedPage is null)
        {
            return false;
        }

        var used = false;
        foreach (var widget in SelectedPage.Widgets)
        {
            if (!string.Equals(widget.Model.KeyShortcut, key, StringComparison.OrdinalIgnoreCase)
                || widget.Presentation != WidgetPresentation.Button)
            {
                continue;
            }

            used = true;
            if (widget.Kind == VirtualConsoleWidgetKind.FlashButton)
            {
                widget.Press();
            }
            else
            {
                widget.Activate();
            }
        }

        return used;
    }

    /// <summary>Key released: ends flashes held by that key.</summary>
    public void ShortcutUp(string key)
    {
        foreach (var widget in _heldFlashes.ToList())
        {
            if (string.Equals(widget.Model.KeyShortcut, key, StringComparison.OrdinalIgnoreCase))
            {
                widget.ReleaseFlash();
            }
        }
    }

    /// <summary>Ends every held flash: window deactivation, view unloaded, page/mode switch (safety rule 5).</summary>
    public void ReleaseFlashes()
    {
        foreach (var widget in _heldFlashes.ToList())
        {
            widget.ReleaseFlash();
        }
    }

    internal void OnFlashHeld(ConsoleWidgetViewModel widget, bool held)
    {
        if (held)
        {
            _heldFlashes.Add(widget);
        }
        else
        {
            _heldFlashes.Remove(widget);
        }
    }

    internal void OnWidgetEdited()
    {
        OnPropertyChanged(nameof(SelectedTargetOptions));
        _projects.MarkChanged(affectsPlayback: false);
    }

    internal string? FindTargetName(ConsoleWidget widget, Guid id)
    {
        var names = widget switch
        {
            ShowWidget => _showNames,
            GroupLevelWidget => _groupNames,
            _ => _functionNames,
        };
        return names.TryGetValue(id, out var name) ? name : null;
    }

    partial void OnSelectedPageChanged(VirtualConsolePageViewModel? oldValue, VirtualConsolePageViewModel? newValue)
    {
        ReleaseFlashes();
        if (oldValue is { IsRenaming: true })
        {
            CommitRename(oldValue);
        }

        SelectedWidget = null;
        if (newValue is not null)
        {
            IsShowsTabSelected = false;
            foreach (var widget in newValue.Widgets)
            {
                widget.Refresh();
            }
        }
    }

    partial void OnSelectedWidgetChanged(ConsoleWidgetViewModel? oldValue, ConsoleWidgetViewModel? newValue)
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

    partial void OnIsEditModeChanged(bool value)
    {
        ReleaseFlashes();
        if (!value)
        {
            SelectedWidget = null;
        }
    }

    partial void OnIsOperatorModeChanged(bool value)
    {
        if (value)
        {
            IsEditMode = false;
            if (SelectedPage is { IsRenaming: true } page)
            {
                CancelRename(page);
            }
        }
    }

    private void AddPageViewModel(ConsolePage page)
    {
        var vm = new VirtualConsolePageViewModel(this, page);
        Pages.Add(vm);
        SelectedPage = vm;
        RaisePageCountChanged();
        _projects.MarkChanged(affectsPlayback: false);
    }

    private void RaisePageCountChanged()
    {
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowCanvas));
        OnPropertyChanged(nameof(ShowNoConsoleState));
    }

    private void PlaceWidget(VirtualConsolePageViewModel page, ConsoleWidget widget)
    {
        _projects.Current.AddConsoleWidget(page.Id, widget);
        var vm = new ConsoleWidgetViewModel(this, page, widget);
        page.Widgets.Add(vm);
        OnPropertyChanged(nameof(IsSelectedPageEmpty));
        IsEditMode = true;
        SelectWidget(vm);
        _projects.MarkChanged(affectsPlayback: false);
    }

    private static ConsoleWidget CreateWidget(VirtualConsoleWidgetKind kind)
    {
        ConsoleWidget widget = kind switch
        {
            VirtualConsoleWidgetKind.FunctionButton => new FunctionWidget(),
            VirtualConsoleWidgetKind.FlashButton => new FunctionWidget { Mode = FunctionWidgetMode.Flash },
            VirtualConsoleWidgetKind.ShowButton => new ShowWidget(),
            VirtualConsoleWidgetKind.GroupLevel => new GroupLevelWidget(),
            VirtualConsoleWidgetKind.Master => new MasterWidget(),
            VirtualConsoleWidgetKind.Blackout => new BlackoutWidget(),
            VirtualConsoleWidgetKind.ReleaseAll => new ReleaseAllWidget(),
            VirtualConsoleWidgetKind.Speed => new SpeedWidget(),
            _ => new LabelWidget { Width = 4, Height = 1 },
        };
        widget.Label = DefaultLabel(kind);
        return widget;
    }

    private void Reload()
    {
        ReleaseFlashes();
        RebuildTargets();
        SelectedWidget = null;
        IsEditMode = false;
        Pages.Clear();
        foreach (var page in _projects.Current.ConsolePages)
        {
            Pages.Add(new VirtualConsolePageViewModel(this, page));
        }

        SelectedPage = Pages.FirstOrDefault();
        RaisePageCountChanged();
        OnPropertyChanged(nameof(IsSelectedPageEmpty));
    }

    private void RebuildTargets()
    {
        var project = _projects.Current;
        _functionNames.Clear();
        foreach (var function in project.Functions)
        {
            _functionNames[function.Id] = function.Name;
        }

        _showNames.Clear();
        foreach (var show in project.Shows)
        {
            _showNames[show.Id] = show.Name;
        }

        _groupNames.Clear();
        foreach (var group in project.Groups)
        {
            _groupNames[group.Id] = group.Name;
        }

        FunctionOptions.Clear();
        SpeedOptions.Clear();
        SpeedOptions.Add(new VirtualConsoleTargetOption(Guid.Empty, "All functions"));
        foreach (var function in project.FunctionListItems)
        {
            var option = new VirtualConsoleTargetOption(function.Id, function.Name);
            FunctionOptions.Add(option);
            SpeedOptions.Add(option);
        }

        ShowOptions.Clear();
        foreach (var show in project.Shows)
        {
            ShowOptions.Add(new VirtualConsoleTargetOption(show.Id, show.Name));
        }

        GroupOptions.Clear();
        foreach (var group in project.Groups)
        {
            GroupOptions.Add(new VirtualConsoleTargetOption(group.Id, group.Name));
        }

        OnPropertyChanged(nameof(SelectedTargetOptions));
    }

    private void RefreshWidgets()
    {
        if (SelectedPage is null)
        {
            return;
        }

        foreach (var widget in SelectedPage.Widgets)
        {
            widget.Refresh();
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            RefreshPlaybackState();
        }
        else
        {
            _uiContext.Post(_ => RefreshPlaybackState(), null);
        }
    }

    private void RefreshPlaybackState()
    {
        // Raised for every fader move: only the live state of each widget is re-read (not names/targets/layout).
        if (SelectedPage is not null)
        {
            foreach (var widget in SelectedPage.Widgets)
            {
                widget.RefreshState();
            }
        }

        ShowPlayer.Refresh();
    }
}
