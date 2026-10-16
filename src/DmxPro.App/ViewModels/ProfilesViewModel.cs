using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;
using Wpf.Ui.Controls;

namespace DmxPro.App.ViewModels;

/// <summary>Fixture library: browse profiles, inspect modes/channels, import QLC+ and DMX Pro profiles.</summary>
public sealed partial class ProfilesViewModel : PageViewModel
{
    private readonly ProfileLibraryService _libraryService;
    private readonly IFileDialogService _fileDialogs;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private FixtureProfile? _selectedProfile;

    [ObservableProperty]
    private FixtureMode? _selectedMode;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Success;

    public ProfilesViewModel(ProfileLibraryService libraryService, IFileDialogService fileDialogs)
    {
        _libraryService = libraryService;
        _fileDialogs = fileDialogs;
        _libraryService.LibraryChanged += (_, _) => Refresh();
    }

    public override string Title => "Fixture Library";

    public override string Subtitle => "Profiles define what every channel of a light does. Import QLC+ (.qxf) files or DMX Pro profiles.";

    public ObservableCollection<FixtureProfile> Profiles { get; } = [];

    /// <summary>Manufacturer → model tree shown on the left.</summary>
    public ObservableCollection<ManufacturerNode> Manufacturers { get; } = [];

    public ObservableCollection<FixtureMode> Modes { get; } = [];

    public ObservableCollection<ChannelRowViewModel> Channels { get; } = [];

    public bool HasSelection => SelectedProfile is not null;

    public override void OnNavigatedTo() => Refresh();

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedProfileChanged(FixtureProfile? value)
    {
        Modes.Clear();
        foreach (var mode in value?.Modes ?? [])
        {
            Modes.Add(mode);
        }

        SelectedMode = Modes.LastOrDefault();
    }

    partial void OnSelectedModeChanged(FixtureMode? value)
    {
        Channels.Clear();
        if (value is null || SelectedProfile is null)
        {
            return;
        }

        var number = 1;
        foreach (var channel in SelectedProfile.ResolveChannels(value))
        {
            Channels.Add(new ChannelRowViewModel(number++, channel));
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var files = _fileDialogs.ShowOpenProfiles();
        if (files.Count == 0)
        {
            return;
        }

        var (imported, issues) = await _libraryService.ImportAsync(files);
        var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
        StatusSeverity = errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        StatusMessage = imported.Count switch
        {
            0 => $"Nothing imported. {string.Join(" ", errors.Select(e => e.Message))}",
            _ => $"Imported {imported.Count} profile(s). Imported profiles are unverified — test each channel on the real light." +
                 (errors.Count > 0 ? $" {errors.Count} file(s) failed: {string.Join(" ", errors.Select(e => e.Message))}" : string.Empty),
        };

        if (imported.Count > 0)
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == imported[^1].Id);
        }
    }

    private void Refresh()
    {
        var selectedId = SelectedProfile?.Id;
        var query = SearchText.Trim();
        Profiles.Clear();
        foreach (var profile in _libraryService.Library.Profiles.Where(p =>
                     query.Length == 0 ||
                     p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     p.Type.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            Profiles.Add(profile);
        }

        Manufacturers.Clear();
        foreach (var node in ManufacturerNode.Build(Profiles, string.Empty))
        {
            Manufacturers.Add(node);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
    }
}
