using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using Microsoft.Win32;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Settings page: app-level preferences saved by <see cref="AppSettingsService"/> as soon as they change.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly AppSettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IDialogService _dialogs;
    private readonly IFolderPickerService _folders;
    private readonly AppPaths _paths;
    private readonly SynchronizationContext? _ui;

    [ObservableProperty]
    private SettingsSectionItem _selectedSection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveProfileFolderCommand))]
    private string? _selectedProfileFolder;

    /// <summary>Load or save problem shown at the top of the page (never only logged).</summary>
    [ObservableProperty]
    private string? _message;

    public SettingsViewModel(
        AppSettingsService settings,
        IThemeService theme,
        IDialogService dialogs,
        IFolderPickerService folders,
        AppPaths paths)
    {
        _settings = settings;
        _theme = theme;
        _dialogs = dialogs;
        _folders = folders;
        _paths = paths;
        _ui = SynchronizationContext.Current;

        Sections =
        [
            new(SettingsSection.Appearance, "Appearance", "ThemeDark"),
            new(SettingsSection.Safety, "Safety", "Warning"),
            new(SettingsSection.ShowPlayback, "Show playback", "Timeline"),
            new(SettingsSection.Engine, "Engine", "Universe"),
            new(SettingsSection.Project, "Project & folders", "Folder"),
            new(SettingsSection.About, "About", "About"),
        ];
        _selectedSection = Sections[0];
        ProfileFolders = new ObservableCollection<string>(S.ProfileFolders);
        Message = _settings.LoadWarning;

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Raised after the theme was applied (startup, setting change, Windows theme change).</summary>
    public event EventHandler? ThemeApplied;

    public override string Title => "Settings";

    public override string Subtitle => "Theme, safety, playback and folder preferences.";

    public IReadOnlyList<SettingsSectionItem> Sections { get; }

    public IReadOnlyList<SettingOption<ThemeModeSetting>> ThemeOptions { get; } =
    [
        new(ThemeModeSetting.Dark, "Dark", "Best for dark venues (default)."),
        new(ThemeModeSetting.Light, "Light", "Best in daylight or bright rooms."),
        new(ThemeModeSetting.FollowWindows, "Follow Windows", "Uses the Windows app mode and follows it when it changes."),
    ];

    public IReadOnlyList<SettingOption<SafeFrameSetting>> SafeFrameOptions { get; } =
    [
        new(SafeFrameSetting.Blackout, "Blackout (all channels 0)", "Every light goes dark when DMX Pro closes."),
        new(SafeFrameSetting.ProfileDefaults, "Profile defaults", "Every channel goes to the default value from its fixture profile."),
    ];

    public IReadOnlyList<SettingOption<ShowOverrideModeSetting>> OverrideModeOptions { get; } =
    [
        new(
            ShowOverrideModeSetting.MuteWholeShow,
            "Mute whole show (default)",
            "While any console button or fader is active, the running show is muted on all lights. " +
            "Its timeline keeps running and reappears at the current position when you release the console."),
        new(
            ShowOverrideModeSetting.OnlyTouchedLights,
            "Only lights the console touches",
            "Console functions override only the channels they control; the rest of the show stays visible."),
    ];

    public bool IsDarkTheme => _theme.IsDark;

    public ThemeModeSetting ThemeMode
    {
        get => S.Theme;
        set
        {
            if (S.Theme != value)
            {
                Update(s => s.Theme = value);
                ApplyTheme();
                OnPropertyChanged();
            }
        }
    }

    public SafeFrameSetting SafeFrameOnExit
    {
        get => S.SafeFrameOnExit;
        set => Set(S.SafeFrameOnExit, value, s => s.SafeFrameOnExit = value);
    }

    public bool AllowStrobe
    {
        get => S.AllowStrobe;
        set => Set(S.AllowStrobe, value, s => s.AllowStrobe = value);
    }

    public double? MaxStrobeRateHz
    {
        get => S.MaxStrobeRateHz;
        set => SetNumber(value, n => S.MaxStrobeRateHz = n);
    }

    public bool ConfirmReleaseAll
    {
        get => S.ConfirmReleaseAll;
        set => Set(S.ConfirmReleaseAll, value, s => s.ConfirmReleaseAll = value);
    }

    public ShowOverrideModeSetting ConsoleOverrideMode
    {
        get => S.ConsoleOverrideMode;
        set => Set(S.ConsoleOverrideMode, value, s => s.ConsoleOverrideMode = value);
    }

    public double? SeekStepSeconds
    {
        get => S.SeekStepSeconds;
        set => SetNumber(value, n => S.SeekStepSeconds = n);
    }

    public string DmxRefreshRateText => $"{S.DmxRefreshRateHz} Hz";

    public bool AutoSaveEnabled
    {
        get => S.AutoSaveEnabled;
        set => Set(S.AutoSaveEnabled, value, s => s.AutoSaveEnabled = value);
    }

    public bool ReloadProjectOnDiskChange
    {
        get => S.ReloadProjectOnDiskChange;
        set => Set(S.ReloadProjectOnDiskChange, value, s => s.ReloadProjectOnDiskChange = value);
    }

    public double? AutosaveIntervalMinutes
    {
        get => S.AutosaveIntervalMinutes;
        set => SetNumber(value, n => S.AutosaveIntervalMinutes = n);
    }

    /// <summary>Folder used by Open / Save As (the user's choice or Documents\DmxPro).</summary>
    public string EffectiveProjectFolder => S.DefaultProjectFolder ?? _paths.DefaultProjectsDirectory;

    public bool HasCustomProjectFolder => S.DefaultProjectFolder is not null;

    public ObservableCollection<string> ProfileFolders { get; }

    public string BuiltInProfilesFolder => _paths.BuiltInProfilesDirectory;

    public string UserProfilesFolder => _paths.UserProfilesDirectory;

    public string SettingsFilePath => _settings.FilePath;

    public string VersionText =>
        $"DMX Pro {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?"} (preview)";

    private AppSettings S => _settings.Current;

    [RelayCommand]
    private void BrowseProjectFolder()
    {
        var folder = _folders.PickFolder("Default project folder", EffectiveProjectFolder);
        if (folder is not null)
        {
            Update(s => s.DefaultProjectFolder = folder);
            OnPropertyChanged(nameof(EffectiveProjectFolder));
            OnPropertyChanged(nameof(HasCustomProjectFolder));
        }
    }

    [RelayCommand]
    private void ResetProjectFolder()
    {
        Update(s => s.DefaultProjectFolder = null);
        OnPropertyChanged(nameof(EffectiveProjectFolder));
        OnPropertyChanged(nameof(HasCustomProjectFolder));
    }

    [RelayCommand]
    private void AddProfileFolder()
    {
        var folder = _folders.PickFolder("Add a fixture profile folder", SelectedProfileFolder);
        if (folder is null || ProfileFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ProfileFolders.Add(folder);
        SelectedProfileFolder = folder;
        SaveProfileFolders();
    }

    /// <summary>Removes the folder from the list only; no file is deleted.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveProfileFolder))]
    private void RemoveProfileFolder()
    {
        if (SelectedProfileFolder is { } folder)
        {
            var index = ProfileFolders.IndexOf(folder);
            ProfileFolders.Remove(folder);
            SelectedProfileFolder = ProfileFolders.Count == 0 ? null : ProfileFolders[Math.Min(index, ProfileFolders.Count - 1)];
            SaveProfileFolders();
        }
    }

    private bool CanRemoveProfileFolder() => SelectedProfileFolder is not null;

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        if (!await _dialogs.ConfirmAsync(
                "Reset settings",
                "Reset all preferences on this page to their defaults? Output configurations are kept.",
                "Reset"))
        {
            return;
        }

        _settings.ResetToDefaults();
        Message = _settings.SaveError;
        ProfileFolders.Clear();
        ApplyTheme();
        OnPropertyChanged(string.Empty);
    }

    private void SaveProfileFolders() => Update(s => s.ProfileFolders = [.. ProfileFolders]);

    private void Set<T>(T current, T value, Action<AppSettings> change, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(current, value))
        {
            Update(change);
            OnPropertyChanged(name);
        }
    }

    /// <summary>Number boxes: empty input keeps the old value; the stored value is clamped and shown back.</summary>
    private void SetNumber(double? value, Action<int> change, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (value is { } v && !double.IsNaN(v))
        {
            Update(_ => change((int)Math.Round(Math.Clamp(v, int.MinValue, int.MaxValue))));
        }

        OnPropertyChanged(name);
    }

    private void Update(Action<AppSettings> change)
    {
        _settings.Update(change);
        Message = _settings.SaveError;
    }

    private void ApplyTheme()
    {
        var dark = S.Theme switch
        {
            ThemeModeSetting.Light => false,
            ThemeModeSetting.FollowWindows => SystemThemeProbe.IsWindowsDark(),
            _ => true,
        };

        if (dark != _theme.IsDark)
        {
            _theme.SetDark(dark);
        }

        OnPropertyChanged(nameof(IsDarkTheme));
        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || S.Theme != ThemeModeSetting.FollowWindows)
        {
            return;
        }

        if (_ui is null)
        {
            ApplyTheme();
        }
        else
        {
            _ui.Post(_ => ApplyTheme(), null);
        }
    }
}
