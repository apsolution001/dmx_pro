using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Serialization;
using DmxPro.Core.Validation;
using Wpf.Ui.Controls;

namespace DmxPro.App.ViewModels;

/// <summary>Main window: navigation, project file commands, and the always-visible status bar.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ProjectService _projects;
    private readonly IFileDialogService _fileDialogs;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly IThemeService _theme;
    private readonly ProfileLibraryService _profileLibrary;
    private readonly FixturesViewModel _fixtures;
    private readonly FunctionsViewModel _functions;
    private readonly ShowsViewModel _shows;
    private readonly InputOutputViewModel _inputOutput;
    private readonly SettingsViewModel _settings;
    private readonly AppSettingsService _appSettings;
    private readonly IStageWindowService _stageWindow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private NavigationItem _selectedItem;

    /// <summary>Message of the shell info bar; null hides it. Set through <see cref="ShowInfo"/>.</summary>
    // Only ShowInfo (plus its own auto-hide / close handling) writes InfoMessage and InfoSeverity: a direct write would
    // leave a pending success auto-hide timer running, which could then wipe a later warning (safety rule 10).
    [ObservableProperty]
    private string? _infoMessage;

    /// <summary>Full screen (F11) covers the whole monitor, taskbar included, without a title bar; the window applies it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExitFullScreenCommand))]
    private bool _isFullScreen;

    [ObservableProperty]
    private InfoBarSeverity _infoSeverity = InfoBarSeverity.Informational;

    public ShellViewModel(
        ProjectService projects,
        IFileDialogService fileDialogs,
        IDialogService dialogs,
        AppPaths paths,
        IThemeService theme,
        ProfileLibraryService profileLibrary,
        FixturesViewModel fixtures,
        FunctionsViewModel functions,
        ShowsViewModel shows,
        VirtualConsoleViewModel virtualConsole,
        TestPageViewModel testPage,
        StagePageViewModel stage,
        InputOutputViewModel inputOutput,
        SettingsViewModel settings,
        AppSettingsService appSettings,
        IStageWindowService stageWindow,
        ILiveModeService liveMode)
    {
        InitializeLiveMode(liveMode);
        _stageWindow = stageWindow;
        _inputOutput = inputOutput;
        _virtualConsole = virtualConsole;
        _testPage = testPage;
        _settings = settings;
        _appSettings = appSettings;
        _fixtures = fixtures;
        _functions = functions;
        _shows = shows;
        _theme = theme;
        _profileLibrary = profileLibrary;
        _projects = projects;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _paths = paths;
        _projects.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(FileStatusText));
        };
        _projects.ContentChanged += (_, _) => OnPropertyChanged(nameof(PatchStatusText));
        _settings.ThemeApplied += (_, _) => OnPropertyChanged(nameof(IsDarkTheme));
        _inputOutput.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(InputOutputViewModel.StatusText) or nameof(InputOutputViewModel.State)
                or nameof(InputOutputViewModel.StatusMessage))
            {
                OnPropertyChanged(nameof(OutputStatusText));
                OnPropertyChanged(nameof(OutputState));
                OnPropertyChanged(nameof(OutputStatusToolTip));
            }
        };

        // Order = rail order. NavigateTo takes the index in this list: MainWindow key bindings and the View menu pass
        // 0–5 for Ctrl+1 … Ctrl+6, 6 for Stage (Ctrl+8, added later, so the older shortcuts did not move) and 7 for
        // Settings (Ctrl+7 / Ctrl+,).
        NavigationItems =
        [
            new("Fixtures", "Fixtures", "Ctrl+1", fixtures),
            new("Functions", "Functions", "Ctrl+2", functions),
            new("Shows", "Timeline", "Ctrl+3", shows),
            new("Virtual Console", "Console", "Ctrl+4", virtualConsole),
            new("Test & Preview", "Test", "Ctrl+5", testPage),
            new("Input / Output", "InputOutput", "Ctrl+6", inputOutput),
            new("Stage", "Stage", "Ctrl+8", stage),
            new("Settings", "Settings", "Ctrl+7", settings),
        ];
        _selectedItem = NavigationItems[0];
        InitializeMenu();
        InitializeHistory();
    }

    /// <summary>Raised when the user picks File ▸ Exit; the window closes itself (with the save prompt).</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public bool IsDarkTheme => _theme.IsDark;

    public PageViewModel CurrentPage => SelectedItem.Page;

    public string WindowTitle =>
        $"{(_projects.FilePath is null ? _projects.Current.Name : Path.GetFileNameWithoutExtension(_projects.FilePath))}{(_projects.IsDirty ? " •" : string.Empty)} — DMX Pro";

    /// <summary>Output is always disabled at start (safety rule 1); live text from the Input / Output page.</summary>
    public string OutputStatusText => _inputOutput.StatusText;

    /// <summary>For the status-bar pill's Tag (Disabled / Connecting / Sending / Error).</summary>
    public DmxPro.Output.OutputState OutputState => _inputOutput.State;

    public string OutputStatusToolTip => _inputOutput.StatusMessage is { } error
        ? $"{error} Open Input / Output (Ctrl+6) to fix it."
        : "DMX output starts disabled every time. Configure and enable it in Input / Output (Ctrl+6).";

    /// <summary>The Input / Output page (for Output menu / status bar bindings: EnableOutputCommand, DisableOutputCommand).</summary>
    public InputOutputViewModel InputOutput => _inputOutput;

    /// <summary>The Settings page.</summary>
    public SettingsViewModel Settings => _settings;

    /// <summary>Patch summary. The universe count is only shown once fixtures exist ("0 universes" reads like an error).</summary>
    public string PatchStatusText
    {
        get
        {
            var project = _projects.Current;
            var fixtures = Count(project.Fixtures.Count, "fixture", "fixtures");
            var groups = Count(project.Groups.Count, "group", "groups");
            if (project.Fixtures.Count == 0)
            {
                return $"{fixtures} · {groups}";
            }

            var universes = project.Fixtures.Select(f => f.Universe).Distinct().Count();
            return $"{fixtures} · {Count(universes, "universe", "universes")} · {groups}";
        }
    }

    public string FileStatusText => _projects.FilePath ?? "Not saved yet";

    partial void OnSelectedItemChanging(NavigationItem? oldValue, NavigationItem newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue.Page, newValue.Page))
        {
            oldValue.Page.OnNavigatedFrom();
        }
    }

    partial void OnSelectedItemChanged(NavigationItem value) => value.Page.OnNavigatedTo();

    /// <summary>Navigates to the page at the given index of <see cref="NavigationItems"/> (View menu, Ctrl+1…).</summary>
    [RelayCommand]
    private void NavigateTo(string index)
    {
        if (int.TryParse(index, out var i) && i >= 0 && i < NavigationItems.Count)
        {
            SelectedItem = NavigationItems[i];
        }
    }

    [RelayCommand]
    private void AddFixture()
    {
        SelectedItem = NavigationItems.First(n => n.Page == _fixtures);
        _fixtures.AddFixtureCommand.Execute(null);
    }

    /// <summary>Fixtures ▸ Import fixture profile: imports straight into the library and reports in the info bar.</summary>
    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        var files = _fileDialogs.ShowOpenProfiles();
        if (files.Count == 0)
        {
            return;
        }

        var (imported, issues) = await _profileLibrary.ImportAsync(files);
        var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
        var failures = string.Join(" ", errors.Select(e => e.Message));
        if (imported.Count == 0)
        {
            ShowInfo(InfoBarSeverity.Error, errors.Count > 0 ? $"Nothing imported. {failures}" : "Nothing imported.");
            return;
        }

        ShowInfo(
            errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
            $"Imported {Count(imported.Count, "profile", "profiles")}." +
            (errors.Count > 0 ? $" {Count(errors.Count, "file", "files")} failed: {failures}" : string.Empty),
            autoHide: false);
    }

    [RelayCommand]
    private void SetTheme(string theme)
    {
        // Through Settings so the choice is saved (and "Follow Windows" is replaced by an explicit theme).
        _settings.ThemeMode = theme == "Dark" ? ThemeModeSetting.Dark : ThemeModeSetting.Light;
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    /// <summary>Esc and the floating button leave full screen. Disabled otherwise, so Esc stays free for other uses.</summary>
    [RelayCommand(CanExecute = nameof(IsFullScreen))]
    private void ExitFullScreen() => IsFullScreen = false;

    [RelayCommand]
    private void Exit() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private Task ShowShortcutsAsync() => _dialogs.ShowInfoAsync(
        "Keyboard shortcuts",
        "Project\n" +
        "  Ctrl+N  New project · Ctrl+O  Open · Ctrl+S  Save · Ctrl+Shift+S  Save as\n\n" +
        "Pages\n" +
        "  Ctrl+1 … Ctrl+6  Fixtures, Functions, Shows, Virtual Console, Test & Preview, Input / Output\n" +
        "  Ctrl+7 or Ctrl+,  Settings · Ctrl+8  Stage · Ctrl+Shift+8  Live stage window · Ctrl+M  DMX monitor · F11  Full screen\n\n" +
        "Editing\n" +
        "  Ctrl+Z  Undo · Ctrl+Y or Ctrl+Shift+Z  Redo (any project change; in a text box they undo its typing)\n" +
        "  Ctrl+X / Ctrl+C / Ctrl+V  Cut / copy / paste functions (in the function list)\n" +
        "  Ctrl+D  Duplicate · Del  Delete · F2  Rename (in the page's list, table or timeline)\n" +
        "  Ctrl+A  Select all (in tables and text boxes) · Ctrl+Shift+A  Add fixture\n" +
        "  Ctrl+G  New group · Ctrl+L  Edit channel layout (Fixtures table)\n\n" +
        "Live\n" +
        "  Ctrl+E  Enable DMX output · Ctrl+Shift+E  Disable DMX output\n" +
        "  Ctrl+B  Blackout on/off (output keeps sending) · Shift+Esc  Release all\n" +
        "  F12  Operator mode · Space  Play/pause the show (Shows timeline, Virtual Console)\n" +
        "  Home  Back to the show start (Shows timeline)\n\n" +
        "Not yet available: Ctrl+K search everything, F1 help.");

    [RelayCommand]
    private Task ShowAboutAsync() => _dialogs.ShowInfoAsync(
        "About DMX Pro",
        $"DMX Pro {typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3)} (preview)\n" +
        "A modern DMX lighting controller for RGB/RGBW fixtures.\n\n" +
        "Imported QLC+ fixture definitions come from the QLC+ fixture library (Apache License 2.0).\n" +
        "UI built with WPF-UI (MIT) and CommunityToolkit.Mvvm (MIT).");

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (await ConfirmDiscardChangesAsync())
        {
            _projects.New();
            ShowInfo(InfoBarSeverity.Informational, null);
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var path = _fileDialogs.ShowOpenProject(ProjectsDirectory);
        if (path is null)
        {
            return;
        }

        try
        {
            var issues = await _projects.OpenAsync(path);
            ShowIssues(issues, $"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DmxFileFormatException)
        {
            await _dialogs.ShowErrorAsync("Could not open project", ex.Message);
        }
    }

    [RelayCommand]
    private Task<bool> SaveProjectAsync() =>
        _projects.FilePath is { } path ? SaveToAsync(path) : SaveProjectAsAsync();

    [RelayCommand]
    private Task<bool> SaveProjectAsAsync()
    {
        var path = _fileDialogs.ShowSaveProject(ProjectsDirectory, _projects.Current.Name);
        return path is null ? Task.FromResult(false) : SaveToAsync(path);
    }

    /// <summary>Shows problems found while loading fixture profiles at startup (never silently ignored).</summary>
    public void ReportLibraryIssues(IReadOnlyList<ValidationIssue> issues)
    {
        var problems = issues.Where(i => i.Severity != ValidationSeverity.Info).ToList();
        if (problems.Count > 0)
        {
            ShowIssues(problems, "Some fixture profiles could not be loaded.");
        }
    }

    /// <summary>Asks to save unsaved work. Returns false when the user cancels.</summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        // An open scene draft is not in the project yet; settle it first so it isn't silently dropped.
        if (!await _functions.ConfirmCloseEditorAsync() || !await _shows.ConfirmCloseEditorAsync())
        {
            return false;
        }

        if (!_projects.IsDirty)
        {
            return true;
        }

        return await _dialogs.AskSaveChangesAsync(_projects.Current.Name) switch
        {
            SaveChangesChoice.Save => await SaveProjectAsync(),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private async Task<bool> SaveToAsync(string path)
    {
        try
        {
            await _projects.SaveAsync(path);
            ShowIssues([], $"Saved {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Could not save project", ex.Message);
            return false;
        }
    }

    private string ProjectsDirectory => _appSettings.Current.DefaultProjectFolder ?? _paths.DefaultProjectsDirectory;

    private static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues, string successMessage)
    {
        if (issues.Count == 0)
        {
            // Success fades after a few seconds; warnings and errors below stay until closed (safety rule 10).
            ShowInfo(InfoBarSeverity.Success, successMessage);
            return;
        }

        ShowInfo(
            issues.Any(i => i.Severity == ValidationSeverity.Error) ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
            $"{successMessage} {issues.Count} issue(s): " + string.Join(" • ", issues.Take(5).Select(i => i.Message)) +
            (issues.Count > 5 ? " …" : string.Empty));
    }

    // ───────────────────────────── Live stage window + info bar auto-hide ─────────────────────────────

    /// <summary>How long a success message (Saved / Opened …) stays in the info bar.</summary>
    public static readonly TimeSpan SuccessMessageDuration = TimeSpan.FromSeconds(3);

    /// <summary>Pending auto-hide of the current success message; a newer message or closing the bar cancels it.</summary>
    private CancellationTokenSource? _infoAutoHide;

    /// <summary>
    /// Whether the info bar is open. Bound two-way to the bar: its close button sets this to false, which clears the
    /// message, so the same message shown again (a second save) opens the bar again.
    /// </summary>
    [ObservableProperty]
    private bool _isInfoOpen;

    /// <summary>Opens the live stage in its own window, or brings the open one to the front (Ctrl+Shift+8).</summary>
    [RelayCommand]
    private void OpenStageWindow() => _stageWindow.Show();

    /// <summary>
    /// Shows a message in the info bar (null hides it). The bar always reopens, also for a message identical to the
    /// current one. Success messages hide themselves after <see cref="SuccessMessageDuration"/> unless
    /// <paramref name="autoHide"/> is false; warnings and errors stay until the user closes them (safety rule 10).
    /// </summary>
    private void ShowInfo(InfoBarSeverity severity, string? message, bool autoHide = true)
    {
        CancelInfoAutoHide();

        // An identical message would not raise a change: clear first so the bar shows (and restarts its timer) again.
        if (message is not null && message == InfoMessage)
        {
            InfoMessage = null;
        }

        InfoSeverity = severity;
        InfoMessage = message;
        if (message is not null && autoHide && severity == InfoBarSeverity.Success)
        {
            _infoAutoHide = new CancellationTokenSource();
            _ = HideInfoLaterAsync(_infoAutoHide.Token);
        }
    }

    private async Task HideInfoLaterAsync(CancellationToken token)
    {
        // Resumes on the UI thread (captured context); a cancelled delay completes quietly instead of throwing.
        await Task.Delay(SuccessMessageDuration, token)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        if (!token.IsCancellationRequested)
        {
            CancelInfoAutoHide();
            InfoMessage = null;
        }
    }

    private void CancelInfoAutoHide()
    {
        _infoAutoHide?.Cancel();
        _infoAutoHide?.Dispose();
        _infoAutoHide = null;
    }

    partial void OnInfoMessageChanged(string? value) => IsInfoOpen = value is not null;

    partial void OnIsInfoOpenChanged(bool value)
    {
        // Closed with the bar's close button: forget the message (and its timer).
        if (!value && InfoMessage is not null)
        {
            CancelInfoAutoHide();
            InfoMessage = null;
        }
    }
}
