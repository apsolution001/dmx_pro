using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Patching;
using DmxPro.Core.Testing;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One light on the Test &amp; Preview page: its name, patch summary, problems and one fader per channel of its mode.
/// </summary>
public sealed partial class TestFixtureRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    private bool _hasOverrides;

    internal TestFixtureRowViewModel(FixtureHealth health, string detail, Action<TestFixtureRowViewModel> onRelease)
    {
        Fixture = health.Fixture;
        Name = string.IsNullOrWhiteSpace(health.Fixture.Name) ? "(unnamed light)" : health.Fixture.Name;
        Detail = detail;
        ProfileUnverified = health.IsUnverified;
        IsMissingProfile = health.IsMissingProfile || health.IsMissingMode;
        IsDisabled = health.IsDisabled;
        HasOverlap = health.HasOverlap;
        OverlapText = health.HasOverlap ? $"Shares addresses with {string.Join(", ", health.OverlapsWith)}." : null;
        ProblemText = health.ProblemText;
        Universe = health.Fixture.Universe;
        ReleaseCommand = new RelayCommand(() => onRelease(this), () => _hasOverrides);
    }

    public Fixture Fixture { get; }

    public Guid FixtureId => Fixture.Id;

    public string Name { get; }

    /// <summary>"U1 · 1–8 · Generic RGBW · 8ch Full".</summary>
    public string Detail { get; }

    public bool ProfileUnverified { get; }

    /// <summary>The profile or its mode is missing (no faders).</summary>
    public bool IsMissingProfile { get; }

    public bool IsDisabled { get; }

    public bool HasOverlap { get; }

    /// <summary>"Shares addresses with Stage R PAR." when <see cref="HasOverlap"/>.</summary>
    public string? OverlapText { get; }

    /// <summary>Why the light has no or limited faders, or null.</summary>
    public string? ProblemText { get; }

    public bool HasProblem => ProblemText is not null;

    public int Universe { get; }

    /// <summary>
    /// False once the page rebuilt its rows (or the project changed): a stale row's faders must not send anything.
    /// </summary>
    internal bool IsCurrent { get; set; } = true;

    public ObservableCollection<TestChannelViewModel> Channels { get; } = [];

    public bool HasNoChannels => Channels.Count == 0;

    /// <summary>At least one channel of this light is under test.</summary>
    public bool HasOverrides => _hasOverrides;

    /// <summary>Release light: every test value of this light.</summary>
    public IRelayCommand ReleaseCommand { get; }

    public string AutomationName => $"{Name}, {Detail}";

    /// <summary>Recomputes <see cref="HasOverrides"/> from the channels.</summary>
    internal void RefreshOverrides()
    {
        var any = false;
        foreach (var channel in Channels)
        {
            if (channel.IsOverridden)
            {
                any = true;
                break;
            }
        }

        if (any == _hasOverrides)
        {
            return;
        }

        _hasOverrides = any;
        OnPropertyChanged(nameof(HasOverrides));
        ReleaseCommand.NotifyCanExecuteChanged();
    }

    internal bool Matches(string search) =>
        Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Detail.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}
