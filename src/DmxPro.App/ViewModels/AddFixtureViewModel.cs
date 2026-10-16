using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels;

/// <summary>Add Fixture dialog: pick a profile and mode, then universe, address, amount and gap.</summary>
public sealed partial class AddFixtureViewModel : ObservableObject
{
    private readonly IReadOnlyList<FixtureProfile> _allProfiles;
    private readonly Project _project;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(Summary))]
    private FixtureProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChannelCount), nameof(CanAdd), nameof(Summary))]
    private string? _selectedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _name = "PAR";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(Summary))]
    private int _universe = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(Summary))]
    private int _address = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(Summary))]
    private int _amount = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _gap;

    public AddFixtureViewModel(IReadOnlyList<FixtureProfile> profiles, Project project)
    {
        _allProfiles = profiles;
        _project = project;
        RebuildTree();
        SelectedProfile = profiles.FirstOrDefault(p => p.Id == "generic-rgbw") ?? profiles.FirstOrDefault();
    }

    public ObservableCollection<ManufacturerNode> Manufacturers { get; } = [];

    public ObservableCollection<string> Modes { get; } = [];

    public int ChannelCount => SelectedProfile?.FindMode(SelectedMode)?.ChannelCount ?? 0;

    /// <summary>Total channels used, including gaps between fixtures.</summary>
    private int TotalChannels => (ChannelCount * Amount) + (Gap * Math.Max(0, Amount - 1));

    private int LastAddress => Address + TotalChannels - 1;

    public bool CanAdd =>
        SelectedProfile is not null && ChannelCount > 0 && !string.IsNullOrWhiteSpace(Name) &&
        Amount is >= 1 and <= DmxAddress.UniverseSize && DmxAddress.IsValidUniverse(Universe) &&
        DmxAddress.IsValid(Address) && LastAddress <= DmxAddress.Max;

    public string Summary
    {
        get
        {
            if (SelectedProfile is null)
            {
                return "Pick a fixture on the left.";
            }

            if (!DmxAddress.IsValid(Address) || LastAddress > DmxAddress.Max)
            {
                return $"Does not fit: {Amount} × {ChannelCount} ch from address {Address} would end at {LastAddress} (max 512).";
            }

            var verified = SelectedProfile.Verified ? string.Empty : "  ⚠ Profile is unverified — test each channel.";
            return $"{Amount} × {ChannelCount} ch → universe {Universe}, addresses {Address}–{LastAddress}.{verified}";
        }
    }

    partial void OnSearchChanged(string value) => RebuildTree();

    partial void OnSelectedProfileChanged(FixtureProfile? value)
    {
        Modes.Clear();
        foreach (var mode in value?.Modes ?? [])
        {
            Modes.Add(mode.Name);
        }

        SelectedMode = Modes.LastOrDefault();
        if (value is not null)
        {
            Name = value.Model;
        }
    }

    partial void OnSelectedModeChanged(string? value) => FindFreeAddress();

    partial void OnUniverseChanged(int value) => FindFreeAddress();

    partial void OnAmountChanged(int value) => FindFreeAddress();

    /// <summary>Moves the start address to the first gap that fits all fixtures.</summary>
    [RelayCommand]
    private void FindFreeAddress()
    {
        if (ChannelCount == 0 || !DmxAddress.IsValidUniverse(Universe))
        {
            return;
        }

        if (PatchValidator.FindFreeAddress(_project.Fixtures, _project, Universe, Math.Max(1, TotalChannels)) is { } free)
        {
            Address = free;
        }
    }

    public IReadOnlyList<Fixture> Apply() =>
        _project.AddFixtures(SelectedProfile!, SelectedMode!, Amount, Universe, Address, Name.Trim(), Gap);

    private void RebuildTree()
    {
        Manufacturers.Clear();
        foreach (var node in ManufacturerNode.Build(_allProfiles, Search))
        {
            Manufacturers.Add(node);
        }
    }
}
