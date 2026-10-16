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
    private readonly ProfilesViewModel _profiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private NavigationItem _selectedItem;

    [ObservableProperty]
    private string? _infoMessage;

    /// <summary>Full screen hides the title bar and maximizes (F11); the window applies it.</summary>
    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    private InfoBarSeverity _infoSeverity = InfoBarSeverity.Informational;

    public ShellViewModel(
        ProjectService projects,
        IFileDialogService fileDialogs,
        IDialogService dialogs,
        AppPaths paths,
        IThemeService theme,
        ProfilesViewModel profiles)
    {
        _theme = theme;
        _profiles = profiles;
        _projects = projects;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _paths = paths;
        _projects.StateChanged += (_, _) => OnPropertyChanged(nameof(WindowTitle));

        NavigationItems =
        [
            new("Fixtures", SymbolRegular.Lightbulb24, new ComingSoonViewModel(
                "Fixtures", "Patch your lights: profile, mode, universe and start address.", "Phase 2",
                ["Add / duplicate / delete fixtures", "Automatic free-address finder", "Overlap and range warnings", "Groups"])),
            new("Fixture Library", SymbolRegular.Library24, profiles),
            new("Functions", SymbolRegular.Flash24, new ComingSoonViewModel(
                "Functions", "Reusable behaviors: chasers, sequences, color cycles, fades, pulses, flash, strobe.", "Phase 4",
                ["Chaser / sequence editor", "Color cycle & pulse", "Copy / paste / duplicate", "Preview before use"])),
            new("Scenes", SymbolRegular.Color24, new ComingSoonViewModel(
                "Scene Creator", "Design static looks with a color picker and profile-driven channel controls.", "Phase 3",
                ["Color picker with RGBW policy", "Per-channel values", "Fade in / out", "Live stage preview"])),
            new("Virtual Console", SymbolRegular.Board24, new ComingSoonViewModel(
                "Virtual Console", "Your live control surface: buttons, sliders, pages.", "Phase 5",
                ["Drag & drop widgets", "Pages", "Operator lock", "Keyboard shortcuts"])),
            new("Show Timeline", SymbolRegular.Timeline24, new ComingSoonViewModel(
                "Show Timeline", "Build shows to music: audio tracks, functions and scenes placed in time.", "Phase 8",
                ["Music tracks with waveform", "Place existing functions at any time", "Create a function for a selected time range", "Beat markers & snapping"])),
            new("Test & Preview", SymbolRegular.Beaker24, new ComingSoonViewModel(
                "Test & Preview", "Check every channel of a fixture before going live.", "Phase 6",
                ["R / G / B / W tests", "Raw channel faders", "Live DMX monitor", "Address footprint view"])),
            new("Input / Output", SymbolRegular.PlugConnected24, new ComingSoonViewModel(
                "Input / Output", "Connect to real hardware.", "Phase 7",
                ["Art-Net", "sACN (E1.31)", "Enttec DMX USB Pro", "Open DMX (FTDI)"])),
            new("Settings", SymbolRegular.Settings24, new ComingSoonViewModel(
                "Settings", "Theme, safety and folder preferences.", "Phase 9",
                ["Safe frame on exit (blackout / defaults)", "Allow strobe + max strobe rate", "Autosave interval", "Profile folders"])),
        ];
        _selectedItem = NavigationItems[1];
    }

    /// <summary>Raised when the user picks File ▸ Exit; the window closes itself (with the save prompt).</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public bool IsDarkTheme => _theme.IsDark;

    public PageViewModel CurrentPage => SelectedItem.Page;

    public string WindowTitle =>
        $"{(_projects.FilePath is null ? _projects.Current.Name : Path.GetFileNameWithoutExtension(_projects.FilePath))}{(_projects.IsDirty ? " •" : string.Empty)} — DMX Pro";

    /// <summary>Output is always disabled at start (safety rule 1). Real output arrives in phase 7.</summary>
    public string OutputStatusText => "Output disabled";

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
    private async Task ImportProfileAsync()
    {
        SelectedItem = NavigationItems.First(n => n.Page == _profiles);
        await _profiles.ImportCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void SetTheme(string theme)
    {
        _theme.SetDark(theme == "Dark");
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    [RelayCommand]
    private void Exit() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private Task ShowShortcutsAsync() => _dialogs.ShowInfoAsync(
        "Keyboard shortcuts",
        "Ctrl+N   New project\n" +
        "Ctrl+O   Open project\n" +
        "Ctrl+S   Save\n" +
        "Ctrl+Shift+S   Save as\n" +
        "Ctrl+1 … Ctrl+9   Go to page (Fixtures … Settings)\n" +
        "F11   Full screen\n\n" +
        "Coming with later phases: Ctrl+Z/Y undo/redo, Ctrl+C/V/D copy/paste/duplicate, Ctrl+K search, " +
        "B blackout, Esc release all, Space play/pause, F12 operator mode.");

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
            InfoMessage = null;
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        var path = _fileDialogs.ShowOpenProject(_paths.DefaultProjectsDirectory);
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
        var path = _fileDialogs.ShowSaveProject(_paths.DefaultProjectsDirectory, _projects.Current.Name);
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

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues, string successMessage)
    {
        if (issues.Count == 0)
        {
            InfoSeverity = InfoBarSeverity.Success;
            InfoMessage = successMessage;
            return;
        }

        InfoSeverity = issues.Any(i => i.Severity == ValidationSeverity.Error) ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
        InfoMessage = $"{successMessage} {issues.Count} issue(s): " + string.Join(" • ", issues.Take(5).Select(i => i.Message)) +
                      (issues.Count > 5 ? " …" : string.Empty);
    }
}
