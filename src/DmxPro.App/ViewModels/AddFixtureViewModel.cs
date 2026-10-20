using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.Validation;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Add Fixture dialog (QLC+ layout): profile tree on the left; name, mode, universe, address, quantity and gap on the
/// right, with a live footprint preview that blocks overlaps and addresses past 512.
/// </summary>
public sealed partial class AddFixtureViewModel : ObservableObject
{
    internal const string PreferredProfileId = "generic-rgbw";

    private readonly List<FixtureProfile> _allProfiles;
    private readonly Project _project;
    private readonly ProfileLibraryService? _library;
    private readonly IFileDialogService? _files;
    private readonly IDialogService? _dialogs;
    private readonly ProjectService? _projects;

    /// <summary>True while the address follows the next free block; typing an address switches it off.</summary>
    private bool _autoAddress = true;
    private bool _settingAddress;
    private bool _noFreeBlock;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfile), nameof(HasProfile), nameof(IsUnverified), nameof(IsCustomSelected), nameof(IsTemplateSelected))]
    [NotifyCanExecuteChangedFor(nameof(EditLayoutCommand), nameof(EditAsNewLayoutCommand))]
    private ProfileTreeItem? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChannelCount))]
    private ModeOption? _selectedMode;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _universe = 1;

    [ObservableProperty]
    private int _address = 1;

    [ObservableProperty]
    private int _quantity = 1;

    [ObservableProperty]
    private int _gap;

    [ObservableProperty]
    private PatchPlan? _plan;

    [ObservableProperty]
    private string _profileCountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportMessage))]
    private string? _importMessage;

    [ObservableProperty]
    private bool _importFailed;

    public AddFixtureViewModel(
        IReadOnlyList<FixtureProfile> profiles,
        Project project,
        ProfileLibraryService? library = null,
        IFileDialogService? files = null,
        IDialogService? dialogs = null,
        ProjectService? projects = null)
    {
        _allProfiles = [.. profiles];
        _project = project;
        _library = library;
        _files = files;
        _dialogs = dialogs;
        _projects = projects;
        RebuildTree();
        Select(_allProfiles.FirstOrDefault(p => p.Id == PreferredProfileId) ?? Groups.FirstOrDefault()?.Items.FirstOrDefault()?.Profile);
    }

    public ObservableCollection<ProfileTreeGroup> Groups { get; } = [];

    public ObservableCollection<ModeOption> Modes { get; } = [];

    /// <summary>Channels of the selected mode, in DMX order.</summary>
    public ObservableCollection<ModeChannelRow> Channels { get; } = [];

    public FixtureProfile? SelectedProfile => SelectedItem?.Profile;

    public bool HasProfile => SelectedItem is not null;

    public bool IsUnverified => SelectedItem?.IsUnverified ?? false;

    public bool IsTreeEmpty => Groups.Count == 0;

    public string EmptyTreeText => _allProfiles.Count == 0
        ? "No fixture profiles yet. Import a QLC+ (.qxf) or DMX Pro profile."
        : $"No profile matches \"{Search.Trim()}\". Clear the search or import a profile.";

    public int ChannelCount => SelectedMode?.ChannelCount ?? 0;

    /// <summary>Import needs the profile library and a file picker; hidden when the caller did not pass them.</summary>
    public bool CanImport => _library is not null && _files is not null;

    public bool HasImportMessage => !string.IsNullOrEmpty(ImportMessage);

    /// <summary>The channel layout editor needs the library, the project and dialogs; hidden when not passed.</summary>
    public bool CanEditLayouts => _library is not null && _dialogs is not null && _projects is not null;

    /// <summary>The selected profile is a custom layout (made in the editor): it can be edited in place.</summary>
    public bool IsCustomSelected => SelectedProfile is { } profile && ChannelLayout.IsCustom(profile);

    /// <summary>
    /// The selected profile is built-in or imported: only a template, its channel order may not match the user's light.
    /// </summary>
    public bool IsTemplateSelected => SelectedProfile is not null && !IsCustomSelected;

    /// <summary>"Will use Universe 1, addresses 1–8, 9–16." for the current inputs.</summary>
    public string PreviewText => Plan?.Describe() ?? string.Empty;

    /// <summary>Why the fixtures cannot be added (shown in red), or null.</summary>
    public string? Problem
    {
        get
        {
            if (SelectedItem is null)
            {
                return "Pick a fixture profile on the left.";
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Enter a name.";
            }

            if (_noFreeBlock && Plan is { IsValid: false, TotalChannels: <= DmxAddress.UniverseSize })
            {
                return $"Universe {Universe} has no free block of {Plan.TotalChannels} channels. " +
                       "Try another universe or fewer fixtures.";
            }

            return Plan?.Problem;
        }
    }

    public bool HasProblem => Problem is not null;

    public bool CanAdd => !HasProblem && SelectedMode is not null;

    public string AddButtonText => Quantity > 1 ? $"Add {Quantity} fixtures" : "Add fixture";

    /// <summary>Patches the fixtures into the project. Call only after the dialog returned OK.</summary>
    public IReadOnlyList<Fixture> Apply() =>
        _project.AddFixtures(SelectedProfile!, SelectedMode!.Name, Quantity, Universe, Address, Name.Trim(), Gap);

    partial void OnSearchChanged(string value)
    {
        RebuildTree();
        if (SelectedItem is not null)
        {
            // Keep the picked profile highlighted if it is still visible after filtering.
            var visible = FindItem(SelectedItem.Profile.Id);
            if (visible is not null)
            {
                visible.IsSelected = true;
            }
        }
    }

    partial void OnSelectedItemChanged(ProfileTreeItem? oldValue, ProfileTreeItem? newValue)
    {
        // Re-selecting the same model (e.g. after the tree was rebuilt by a search) keeps the chosen mode.
        if (newValue is null || ReferenceEquals(oldValue?.Profile, newValue.Profile))
        {
            return;
        }

        // Follow the model name unless the user typed their own.
        if (string.IsNullOrWhiteSpace(Name) || (oldValue is not null && Name == oldValue.Profile.Model))
        {
            Name = newValue.Profile.Model;
        }

        Modes.Clear();
        foreach (var mode in newValue.Profile.Modes)
        {
            Modes.Add(new ModeOption(mode));
        }

        // The last mode is usually the full one ("8ch Full").
        SelectedMode = Modes.LastOrDefault();
        UpdatePlan();
    }

    partial void OnSelectedModeChanged(ModeOption? value)
    {
        Channels.Clear();
        if (value is not null && SelectedProfile is { } profile)
        {
            var number = 1;
            foreach (var channel in profile.ResolveChannels(value.Mode))
            {
                Channels.Add(new ModeChannelRow(number++, channel.Name, DescribeType(channel), channel.Type == ChannelType.Generic));
            }
        }

        RefreshAddress();
    }

    partial void OnNameChanged(string value) => NotifyValidation();

    partial void OnUniverseChanged(int value) => RefreshAddress();

    partial void OnQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(AddButtonText));
        RefreshAddress();
    }

    partial void OnGapChanged(int value) => RefreshAddress();

    partial void OnAddressChanged(int value)
    {
        if (!_settingAddress)
        {
            _autoAddress = false;
            _noFreeBlock = false;
        }

        UpdatePlan();
    }

    partial void OnPlanChanged(PatchPlan? value)
    {
        OnPropertyChanged(nameof(PreviewText));
        NotifyValidation();
    }

    /// <summary>Moves the start address to the first free block that fits all fixtures, and keeps following it.</summary>
    [RelayCommand]
    private void UseNextFreeAddress()
    {
        _autoAddress = true;
        RefreshAddress();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var paths = _files!.ShowOpenProfiles();
        if (paths.Count == 0)
        {
            return;
        }

        var (imported, issues) = await _library!.ImportAsync(paths);
        foreach (var profile in imported)
        {
            var index = _allProfiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _allProfiles[index] = profile;
            }
            else
            {
                _allProfiles.Add(profile);
            }
        }

        var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message).ToList();
        ImportFailed = errors.Count > 0;
        ImportMessage = errors.Count switch
        {
            0 => imported.Count == 1 ? $"Imported \"{imported[0].DisplayName}\"." : $"Imported {imported.Count} profiles.",
            1 => errors[0],
            _ => $"{errors[0]} (+{errors.Count - 1} more problems)",
        };

        if (imported.Count > 0)
        {
            // Clearing the search rebuilds the tree, so the imported model is visible before it is selected.
            Search = string.Empty;
            RebuildTree();
            Select(imported[^1]);
        }
    }

    /// <summary>Opens the channel layout editor for a new layout (from a template).</summary>
    [RelayCommand(CanExecute = nameof(CanEditLayouts))]
    private void NewLayout() => OpenLayoutEditor(source: null, modeName: null, editSource: false);

    /// <summary>Edits the selected custom layout; lights already using it in the project follow the change.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelectedLayout))]
    private void EditLayout() => OpenLayoutEditor(SelectedProfile, SelectedMode?.Name, editSource: true);

    /// <summary>Copies the selected (built-in) profile's mode into the editor and saves it as a new layout.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyLayout))]
    private void EditAsNewLayout() => OpenLayoutEditor(SelectedProfile, SelectedMode?.Name, editSource: false);

    private bool CanEditSelectedLayout() => CanEditLayouts && IsCustomSelected;

    private bool CanCopyLayout() => CanEditLayouts && HasProfile;

    private void OpenLayoutEditor(FixtureProfile? source, string? modeName, bool editSource)
    {
        var editor = new ChannelLayoutViewModel(_library!, _projects!, _dialogs!, source, modeName, editSource);
        if (!_dialogs!.ShowChannelLayout(editor) || editor.SavedProfile is not { } saved)
        {
            return;
        }

        var index = _allProfiles.FindIndex(p => string.Equals(p.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _allProfiles[index] = saved;
        }
        else
        {
            _allProfiles.Add(saved);
        }

        ImportFailed = false;
        ImportMessage = editSource ? $"Saved \"{saved.DisplayName}\"." : $"Created \"{saved.DisplayName}\".";

        // Clearing the search rebuilds the tree, so the saved layout is visible before it is selected.
        Search = string.Empty;
        RebuildTree();
        Select(saved);
    }

    private void Select(FixtureProfile? profile)
    {
        var item = profile is null ? null : FindItem(profile.Id);
        if (item is not null)
        {
            item.IsSelected = true;
            SelectedItem = item;
        }
    }

    private ProfileTreeItem? FindItem(string profileId) =>
        Groups.SelectMany(g => g.Items).FirstOrDefault(i => string.Equals(i.Profile.Id, profileId, StringComparison.OrdinalIgnoreCase));

    private void RebuildTree()
    {
        Groups.Clear();
        foreach (var group in ProfileTreeGroup.Build(_allProfiles, Search))
        {
            Groups.Add(group);
        }

        var shown = Groups.Sum(g => g.Items.Count);
        var total = _allProfiles.Count;
        ProfileCountText = shown == total
            ? (total == 1 ? "1 profile" : $"{total} profiles")
            : $"{shown} of {total} profiles";
        OnPropertyChanged(nameof(IsTreeEmpty));
        OnPropertyChanged(nameof(EmptyTreeText));
    }

    /// <summary>Re-applies the next free address (when following it) and refreshes the preview.</summary>
    private void RefreshAddress()
    {
        _noFreeBlock = false;
        if (_autoAddress && ChannelCount > 0 && DmxAddress.IsValidUniverse(Universe) && Quantity >= 1 && Gap >= 0)
        {
            var block = Math.Max(1, PatchPlan.BlockChannels(ChannelCount, Quantity, Gap));
            var free = PatchValidator.FindFreeAddress(_project.Fixtures, _project, Universe, block);
            _noFreeBlock = free is null;
            if (free is { } address)
            {
                _settingAddress = true;
                try
                {
                    Address = address;
                }
                finally
                {
                    _settingAddress = false;
                }
            }
        }

        UpdatePlan();
    }

    private void UpdatePlan() =>
        Plan = PatchPlan.Create(Universe, Address, ChannelCount, Quantity, Gap, _project.Fixtures, _project);

    private void NotifyValidation()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(CanAdd));
    }

    private static string DescribeType(ChannelDefinition channel) => channel.Type switch
    {
        ChannelType.Generic => "Unknown",
        ChannelType.ColorMacro => "Color macro",
        ChannelType.Fine => "Fine",
        _ => channel.Type.ToString(),
    };
}
