using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.Validation;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One patched fixture: a row in the fixture table and the object edited in the Properties panel.
/// Setters write straight to the model and tell the page which property changed, so it only refreshes what that
/// property affects.
/// </summary>
public sealed partial class FixtureRowViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Func<IReadOnlyList<FixtureProfile>> _profileOptions;
    private readonly Action<FixtureRowViewModel, string> _onEdited;

    [ObservableProperty]
    private string _issueText = string.Empty;

    [ObservableProperty]
    private ValidationSeverity? _worstIssue;

    [ObservableProperty]
    private string _groupNames = string.Empty;

    /// <summary>Row selection in the fixture table (two-way bound to the DataGrid row), so groups can select rows.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Problem with the universe field, shown under it (empty = none).</summary>
    [ObservableProperty]
    private string _universeIssue = string.Empty;

    /// <summary>Problem with the start address (out of range, past 512, overlap), shown under it (empty = none).</summary>
    [ObservableProperty]
    private string _addressIssue = string.Empty;

    [ObservableProperty]
    private bool _addressIssueIsError;

    public FixtureRowViewModel(
        Fixture fixture,
        Project project,
        Func<IReadOnlyList<FixtureProfile>> profileOptions,
        Action<FixtureRowViewModel, string> onEdited)
    {
        Fixture = fixture;
        _project = project;
        _profileOptions = profileOptions;
        _onEdited = onEdited;
    }

    public Fixture Fixture { get; }

    private FixtureProfile? EmbeddedProfile => _project.FindProfile(Fixture.ProfileId);

    public string Name
    {
        get => Fixture.Name;
        set => Edit(() => Fixture.Name = value.Trim(), string.IsNullOrWhiteSpace(value) || value == Fixture.Name);
    }

    /// <summary>Selected profile, matched by id against the options list (library copies differ from embedded ones).</summary>
    public FixtureProfile? Profile
    {
        get => _profileOptions().FirstOrDefault(p => p.Id == Fixture.ProfileId) ?? EmbeddedProfile;
        set
        {
            if (value is null || value.Id == Fixture.ProfileId)
            {
                return;
            }

            Edit(() =>
            {
                var embedded = _project.FindProfile(value.Id) ?? _project.EmbedProfile(value);
                Fixture.ProfileId = embedded.Id;
                Fixture.ModeName = embedded.Modes.LastOrDefault()?.Name ?? string.Empty;
            });
        }
    }

    public IReadOnlyList<string> ModeNames => EmbeddedProfile?.Modes.Select(m => m.Name).ToList() ?? [];

    public string ModeName
    {
        get => Fixture.ModeName;
        set => Edit(() => Fixture.ModeName = value, string.IsNullOrEmpty(value) || value == Fixture.ModeName);
    }

    public int Universe
    {
        get => Fixture.Universe;
        set => Edit(() => Fixture.Universe = value, value == Fixture.Universe);
    }

    public int StartAddress
    {
        get => Fixture.StartAddress;
        set => Edit(() => Fixture.StartAddress = value, value == Fixture.StartAddress);
    }

    /// <summary><see cref="Universe"/> for the number box (WPF-UI NumberBox binds a nullable double).</summary>
    public double? UniverseNumber
    {
        get => Fixture.Universe;
        set
        {
            if (value is { } v && !double.IsNaN(v))
            {
                Universe = (int)Math.Round(v);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary><see cref="StartAddress"/> for the number box (WPF-UI NumberBox binds a nullable double).</summary>
    public double? AddressNumber
    {
        get => Fixture.StartAddress;
        set
        {
            if (value is { } v && !double.IsNaN(v))
            {
                StartAddress = (int)Math.Round(v);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool Enabled
    {
        get => Fixture.Enabled;
        set => Edit(() => Fixture.Enabled = value, value == Fixture.Enabled);
    }

    public string Notes
    {
        get => Fixture.Notes ?? string.Empty;
        set => Edit(() => Fixture.Notes = value, value == (Fixture.Notes ?? string.Empty));
    }

    public string ProfileName => EmbeddedProfile?.DisplayName ?? $"Missing profile '{Fixture.ProfileId}'";

    public int ChannelCount => EmbeddedProfile?.FindMode(Fixture.ModeName)?.ChannelCount ?? 0;

    public string AddressRange => ChannelCount == 0
        ? Fixture.StartAddress.ToString()
        : $"{Fixture.StartAddress}–{DmxAddress.EndAddress(Fixture.StartAddress, ChannelCount)}";

    /// <summary>e.g. "1–8 · 8 channels".</summary>
    public string FootprintText => ChannelCount switch
    {
        0 => "Unknown footprint (profile or mode missing)",
        1 => $"{Fixture.StartAddress} · 1 channel",
        _ => $"{AddressRange} · {ChannelCount} channels",
    };

    /// <summary>Universe and address for compact lists, e.g. "U1 · 1–8".</summary>
    public string UniverseAddressText => $"U{Fixture.Universe} · {AddressRange}";

    /// <summary>Channel map in DMX order, named from the profile.</summary>
    public IReadOnlyList<ChannelMapRow> Channels
    {
        get
        {
            var profile = EmbeddedProfile;
            var mode = profile?.FindMode(Fixture.ModeName);
            if (profile is null || mode is null)
            {
                return [];
            }

            return profile.ResolveChannels(mode)
                .Select((c, i) => new ChannelMapRow(i + 1, Fixture.StartAddress + i, c.Name, c.Type.ToString(), c.Type == ChannelType.Generic))
                .ToList();
        }
    }

    /// <summary>Re-reads every derived value after the model changed elsewhere (e.g. auto-address).</summary>
    public void RefreshAll() => OnPropertyChanged(string.Empty);

    private void Edit(Action apply, bool skip = false, [CallerMemberName] string property = "")
    {
        if (skip)
        {
            return;
        }

        apply();
        RefreshAll();
        _onEdited(this, property);
    }
}
