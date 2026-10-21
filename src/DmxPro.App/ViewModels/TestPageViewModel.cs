using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.Testing;
using DmxPro.Engine;
using DmxPro.Engine.Testing;
using DmxPro.Output;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Test &amp; Preview page, laid out like a QLC+ Simple Desk: the lights of one universe side by side, each a group of
/// channel faders that show the live output and take over on touch (manual overrides through
/// <see cref="ChannelTestSession"/>), a Grand Master fader, an optional DMX monitor of the same universe and a warnings
/// strip. Test values are never saved, never mark the project dirty and are released when the page is left.
/// </summary>
/// <remarks>
/// <para><b>Ordering with engine updates.</b> Project edits reach the engine through <see cref="EngineRuntimeService"/>,
/// which coalesces them and then raises <see cref="EngineRuntimeService.PatchRefreshed"/> on the UI thread right after
/// <see cref="DmxEngine.RefreshPatch"/>. This page re-applies its overrides in that handler, so the clear/set commands
/// queue after the refresh (whose override copy keeps the old addresses) and the patch lookups already see the new
/// model. A project open raises <see cref="EngineRuntimeService.ProjectLoaded"/> (the engine dropped every override)
/// and the console Release All raises <see cref="EngineConsolePlayback.AllReleased"/>; both only reset the session.
/// </para>
/// <para>Polling (~30 Hz) runs only while the page is visible, on the UI thread.</para>
/// <para><b>Live mode</b> (<see cref="ILiveModeService"/>): only the Virtual Console drives the lights. Turning it on
/// releases every test value (the engine refuses new ones, <see cref="ChannelTestSession.Set"/> returns false); the
/// faders, paste and "Save as function…" are disabled and a banner says why. The Grand Master stays usable (it belongs
/// to the console too). Test values are not restored when live mode is turned off.</para>
/// </remarks>
public sealed partial class TestPageViewModel : PageViewModel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>Warnings are re-evaluated every this many polls (~3 Hz).</summary>
    private const int WarningEveryPolls = 10;

    private readonly ProjectService _projects;
    private readonly DmxEngine _engine;
    private readonly ChannelTestSession _session;
    private readonly InputOutputViewModel _inputOutput;
    private readonly EngineConsolePlayback _playback;
    private readonly LightClipboardService _clipboard;
    private readonly ILogger<TestPageViewModel> _logger;
    private readonly SynchronizationContext? _uiContext;
    private readonly IDialogService _dialogs;
    private readonly ILiveModeService _liveMode;
    private readonly bool[] _monitorOverridden = new bool[DmxAddress.UniverseSize];
    private readonly List<TestWarningViewModel> _fixtureWarnings = [];
    private PatchHealth? _health;

    /// <summary><see cref="ProjectService.Revision"/> the rows were last checked against (-1 = never built).</summary>
    private long _checkedRevision = -1;

    /// <summary><see cref="PatchFingerprint"/> of the project the rows were built from.</summary>
    private int _fingerprint;

    /// <summary>The rows must be rebuilt on the next visit (never built, or a project was opened).</summary>
    private bool _rowsStale = true;

    /// <summary>What the warnings strip was last built from; the strip is only rebuilt when this changes.</summary>
    private WarningInputs _warningInputs;
    private bool _hasWarningInputs;
    private bool _isVisible;
    private CancellationTokenSource? _poll;
    private int _pollCount;
    private int _pollFailures;
    private string? _pollError;
    private double _grandMasterPercent = 100;

    /// <summary>Consecutive failed polls before the "live values stopped updating" warning shows.</summary>
    private const int PollFailuresBeforeWarning = 3;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TestFixtureRowViewModel? _selectedFixture;

    [ObservableProperty]
    private int _selectedUniverse = 1;

    /// <summary>Off by default: the desk gets the full width (QLC+ Simple Desk look).</summary>
    [ObservableProperty]
    private bool _isMonitorVisible;

    /// <summary>Page info bar text (e.g. "Saved scene 'Test look 1' to Functions."); shown while <see cref="IsNoticeOpen"/>.</summary>
    [ObservableProperty]
    private string _noticeMessage = string.Empty;

    [ObservableProperty]
    private bool _isNoticeOpen;

    public TestPageViewModel(
        ProjectService projects,
        DmxEngine engine,
        ChannelTestSession session,
        EngineRuntimeService runtime,
        EngineConsolePlayback playback,
        InputOutputViewModel inputOutput,
        LightClipboardService clipboard,
        IDialogService dialogs,
        ILiveModeService liveMode,
        ILogger<TestPageViewModel> logger)
    {
        _projects = projects;
        _dialogs = dialogs;
        _liveMode = liveMode;
        _engine = engine;
        _session = session;
        _inputOutput = inputOutput;
        _playback = playback;
        _clipboard = clipboard;
        _logger = logger;
        _uiContext = SynchronizationContext.Current;

        for (var address = DmxAddress.Min; address <= DmxAddress.Max; address++)
        {
            MonitorCells.Add(new DmxMonitorCellViewModel(address));
        }

        ReleaseAllTestsCommand = new RelayCommand(ReleaseAllTests, () => HasOverrides);
        SaveAsFunctionCommand = new RelayCommand(SaveAsFunction, () => HasOverrides && !IsLive);
        ResetGrandMasterCommand = new RelayCommand(() => GrandMasterPercent = 100);
        CopyLightCommand = new RelayCommand<TestFixtureRowViewModel?>(CopyLight, row => (row ?? SelectedFixture) is { HasNoChannels: false });
        PasteLightCommand = new RelayCommand<TestFixtureRowViewModel?>(PasteLight, row => !IsLive && _clipboard.HasValues && (row ?? SelectedFixture) is not null);
        PasteToAllCommand = new RelayCommand(PasteToAll, () => !IsLive && _clipboard.HasValues && FilteredFixtures.Count > 0);
        clipboard.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CopiedText));
            PasteLightCommand.NotifyCanExecuteChanged();
            PasteToAllCommand.NotifyCanExecuteChanged();
        };

        // The page starts hidden: no test values until it is shown.
        _session.Deactivate();
        runtime.ProjectLoaded += (_, _) => OnProjectLoaded();
        runtime.PatchRefreshed += (_, _) => OnPatchRefreshed();
        playback.AllReleased += (_, _) => OnUiThread(OnEngineReleasedAll);
        liveMode.LiveModeChanged += (_, _) => OnUiThread(OnLiveModeChanged);
    }

    public override string Title => "Test & Preview";

    public override string Subtitle => "Check every channel of your lights before going live.";

    /// <summary>Every patched light in patch order (universe, start address, name).</summary>
    public ObservableCollection<TestFixtureRowViewModel> Fixtures { get; } = [];

    /// <summary><see cref="Fixtures"/> of <see cref="SelectedUniverse"/>, filtered by <see cref="SearchText"/>.</summary>
    public ObservableCollection<TestFixtureRowViewModel> FilteredFixtures { get; } = [];

    public bool HasNoFixtures => Fixtures.Count == 0;

    /// <summary>Lights exist but none is in the shown universe or matches the search.</summary>
    public bool HasNoMatches => Fixtures.Count > 0 && FilteredFixtures.Count == 0;

    /// <summary>Title of the empty desk when <see cref="HasNoMatches"/>.</summary>
    public string NoMatchesTitle => SearchText.Trim().Length > 0 ? "No matching lights" : $"No lights in Universe {SelectedUniverse}";

    /// <summary>Hint of the empty desk when <see cref="HasNoMatches"/>.</summary>
    public string NoMatchesText => SearchText.Trim().Length > 0
        ? $"No light in Universe {SelectedUniverse} matches the search. Clear the search box or pick another universe."
        : "Pick another universe above, or patch lights to this one on the Fixtures page (Ctrl+1).";

    /// <summary>
    /// Grand Master 0–100 % (the engine's global master, shared with the Virtual Console). Scales intensity channels only.
    /// </summary>
    public double GrandMasterPercent
    {
        get => _grandMasterPercent;
        set
        {
            var percent = double.IsNaN(value) ? 0 : Math.Clamp(Math.Round(value), 0, 100);
            _playback.SetGrandMaster(percent / 100);
            SetGrandMasterShown(percent);
        }
    }

    /// <summary>
    /// Copies a light's current fader values (the row given, else the selected light) to the app clipboard, so they can
    /// be pasted onto other lights here or in a scene editor.
    /// </summary>
    public IRelayCommand<TestFixtureRowViewModel?> CopyLightCommand { get; }

    /// <summary>Pastes the copied values onto a light (the row given, else the selected light) as test values.</summary>
    public IRelayCommand<TestFixtureRowViewModel?> PasteLightCommand { get; }

    /// <summary>Pastes the copied values onto every light shown on the desk (this universe, matching the search).</summary>
    public IRelayCommand PasteToAllCommand { get; }

    /// <summary>"Copied: PAR 1" while something is copied, else null.</summary>
    public string? CopiedText => _clipboard.Current is { } copied ? $"Copied: {copied.SourceName}" : null;

    /// <summary>Sets the Grand Master back to 100 % (the "GM" button under the fader).</summary>
    public IRelayCommand ResetGrandMasterCommand { get; }

    public ObservableCollection<TestWarningViewModel> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public int OverriddenCount => _session.Count;

    public bool HasOverrides => _session.Count > 0;

    /// <summary>"3 channels under test" / "No test values".</summary>
    public string OverrideSummary => _session.Count switch
    {
        0 => "No test values",
        1 => "1 channel under test",
        var n => $"{n} channels under test",
    };

    /// <summary>Releases this page's test values only (not the engine Release All).</summary>
    public IRelayCommand ReleaseAllTestsCommand { get; }

    /// <summary>
    /// "Save as function…": saves the channels under test (their test values, before Grand Master and Blackout) as a
    /// new scene in the Functions list. Disabled while nothing is under test.
    /// </summary>
    public IRelayCommand SaveAsFunctionCommand { get; }

    public string SaveAsFunctionToolTip => IsLive ? "Not available in live mode: test values are off"
        : HasOverrides ? "Save as function…: save the channels under test as a new scene in the Functions list"
        : "Set some channels first";

    /// <summary>Live mode is on: the desk shows the output but takes no test values.</summary>
    public bool IsLive => _liveMode.IsLive;

    /// <summary>The channel faders accept input (not in live mode).</summary>
    public bool IsDeskEnabled => !IsLive;

    /// <summary>Banner text while <see cref="IsLive"/>.</summary>
    public static string LiveModeBanner => "Live mode — only the Virtual Console controls the lights. Test values are off.";

    /// <summary>Project universes plus any universe a light is patched to, ascending.</summary>
    public ObservableCollection<int> Universes { get; } = [];

    /// <summary>Always 512 cells, created once and updated in place.</summary>
    public ObservableCollection<DmxMonitorCellViewModel> MonitorCells { get; } = [];

    public override void OnNavigatedTo()
    {
        _isVisible = true;
        _session.Activate();

        // Switching pages must be quick: nothing to check when the project did not change since the last visit, and
        // only a change to what the rows show (patch, profiles, names) rebuilds them; a scene edit does not.
        if (_rowsStale || _checkedRevision != _projects.Revision)
        {
            _checkedRevision = _projects.Revision;
            if (_rowsStale || PatchFingerprint(_projects.Current) != _fingerprint)
            {
                Rebuild();
            }
        }

        RefreshWarnings();
        StartPolling();
    }

    public override void OnNavigatedFrom()
    {
        _isVisible = false;
        IsNoticeOpen = false;
        StopPolling();

        // Owner decision 2026-10-19: leaving the Test page releases every test value, and none can be set until it is
        // shown again (a late slider event from the hidden view is refused).
        _session.Deactivate();
        MarkAllReleased();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Asks for a name (and optional fades), then saves every channel under test, in patch order, as a new library
    /// scene. The values are the test values (what the lights are asked for), not the output after Grand Master or
    /// Blackout. The test values stay as they are.
    /// </summary>
    private void SaveAsFunction()
    {
        var values = new List<SceneValue>();
        foreach (var row in Fixtures)
        {
            foreach (var channel in row.Channels)
            {
                if (channel.IsOverridden)
                {
                    values.Add(new SceneValue { FixtureId = channel.FixtureId, ChannelKey = channel.Key, Value = channel.ByteValue });
                }
            }
        }

        if (values.Count == 0)
        {
            return;
        }

        var project = _projects.Current;
        var lights = values.Select(v => v.FixtureId).Distinct().Count();
        var dialog = new SaveFunctionDialogViewModel(
            "Save as function",
            $"Saves the {Plural(values.Count, "channel", "channels")} under test on {Plural(lights, "light", "lights")} as a new scene in the Functions list.",
            project.NextFunctionName("Test look"),
            showFades: true,
            project.IsFunctionNameTaken);
        if (!_dialogs.ShowSaveFunction(dialog) || !ReferenceEquals(project, _projects.Current))
        {
            return;
        }

        var scene = project.CreateSceneFromValues(dialog.TrimmedName, values, dialog.FadeIn, dialog.FadeOut);
        _projects.MarkChanged();
        NoticeMessage = $"Saved scene '{scene.Name}' to Functions.";

        // Re-open even when a previous notice is still showing, so the info bar announces the new text.
        IsNoticeOpen = false;
        IsNoticeOpen = true;
    }

    private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    private void CopyLight(TestFixtureRowViewModel? row)
    {
        if ((row ?? SelectedFixture) is not { HasNoChannels: false } source)
        {
            return;
        }

        var snapshot = _engine.GetSnapshot();
        _clipboard.Copy(CopiedLightValues.Create(
            source.Fixture.ProfileId,
            source.Name,
            source.Channels.Select(c => (c.Channel, RequestedValue(c, snapshot)))));
    }

    /// <summary>
    /// What the light is asked to do on a channel, never what goes out: the test value while under test, otherwise
    /// the engine's merged value before Grand Master, Blackout and strobe safety (else a paste would apply them twice
    /// and a scene would store dimmed values).
    /// </summary>
    private static byte RequestedValue(TestChannelViewModel channel, EngineSnapshot? snapshot) =>
        channel.IsOverridden || !channel.IsAvailable || snapshot is null
            ? channel.ByteValue
            : snapshot.GetRequestedValue(channel.Universe, channel.DmxAddress);

    private void PasteLight(TestFixtureRowViewModel? row)
    {
        if ((row ?? SelectedFixture) is { } target)
        {
            PasteOnto(target);
        }
    }

    private void PasteToAll()
    {
        foreach (var row in FilteredFixtures)
        {
            PasteOnto(row);
        }
    }

    /// <summary>Sets the matching channels as test values (profile-matched; unavailable channels are skipped).</summary>
    private void PasteOnto(TestFixtureRowViewModel row)
    {
        if (_clipboard.Current is not { } copied || row.Channels.Count == 0)
        {
            return;
        }

        var channels = row.Channels.Select(c => c.Channel).ToList();
        foreach (var (index, value) in copied.MatchTo(row.Fixture.ProfileId, channels))
        {
            var channel = row.Channels[index];
            if (channel.IsAvailable)
            {
                channel.Value = value;
            }
        }
    }

    partial void OnIsMonitorVisibleChanged(bool value)
    {
        if (value)
        {
            PollMonitor(_engine.GetSnapshot());
        }
    }

    private void SetGrandMasterShown(double percent)
    {
        if (_grandMasterPercent != percent)
        {
            _grandMasterPercent = percent;
            OnPropertyChanged(nameof(GrandMasterPercent));
        }
    }

    partial void OnSelectedFixtureChanged(TestFixtureRowViewModel? oldValue, TestFixtureRowViewModel? newValue)
    {
        CopyLightCommand.NotifyCanExecuteChanged();
        PasteLightCommand.NotifyCanExecuteChanged();
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
            if (newValue.Universe != SelectedUniverse && Universes.Contains(newValue.Universe))
            {
                SelectedUniverse = newValue.Universe;
                return; // the universe change refreshes the monitor
            }
        }

        RefreshMonitorPatch();
    }

    partial void OnSelectedUniverseChanged(int value)
    {
        ApplyFilter();
        RefreshMonitorPatch();
        PollMonitor(_engine.GetSnapshot());
    }

    // ───────────────────────────── Engine / project events ─────────────────────────────

    private void OnProjectLoaded()
    {
        // The load normally dropped every override already (the session sees that through the engine release
        // generation); ReleaseAll also clears anything still tracked, e.g. when the load failed.
        _session.ReleaseAll();
        _rowsStale = true;
        if (_isVisible)
        {
            Rebuild();
            RefreshWarnings();
        }
        else
        {
            RefreshOverrideState();
        }
    }

    private void OnPatchRefreshed()
    {
        // Runs right after DmxEngine.RefreshPatch on the UI thread: commands queue after the refresh.
        _session.Reapply();
        if (!_isVisible)
        {
            RefreshOverrideState();
            return;
        }

        // Always rebuild: availability comes from the engine patch, which may have caught up with an edit the rows
        // were built before (same project signature, different engine state).
        Rebuild();
        RefreshWarnings();
    }

    private void OnEngineReleasedAll()
    {
        // Engine Release All cleared every override, ours included. ReleaseAll (not Reset) so nothing tracked can be left
        // in the engine if this runs late or the generation check missed it.
        _session.ReleaseAll();
        MarkAllReleased();
    }

    /// <summary>
    /// Live on: every test value is released (the engine dropped them already; this clears the session and the faders).
    /// Live off: nothing is restored, the desk just accepts input again.
    /// </summary>
    private void OnLiveModeChanged()
    {
        if (_liveMode.IsLive)
        {
            OnEngineReleasedAll();
        }

        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsDeskEnabled));
        OnPropertyChanged(nameof(SaveAsFunctionToolTip));
        SaveAsFunctionCommand.NotifyCanExecuteChanged();
        PasteLightCommand.NotifyCanExecuteChanged();
        PasteToAllCommand.NotifyCanExecuteChanged();
    }

    // ───────────────────────────── Commands from rows / channels ─────────────────────────────

    private bool OnChannelSet(TestChannelViewModel channel, byte value)
    {
        // Stale rows (after a rebuild / project open) and a hidden page never send anything.
        if (!_isVisible || !channel.Row.IsCurrent || !_session.Set(channel.FixtureId, channel.Key, value))
        {
            return false;
        }

        channel.Row.RefreshOverrides();
        RefreshOverrideState();
        return true;
    }

    private void OnChannelRelease(TestChannelViewModel channel)
    {
        _session.Release(channel.FixtureId, channel.Key);
        channel.MarkReleased();
        channel.Row.RefreshOverrides();
        RefreshOverrideState();
    }

    private void OnRowRelease(TestFixtureRowViewModel row)
    {
        _session.ReleaseFixture(row.FixtureId);
        foreach (var channel in row.Channels)
        {
            channel.MarkReleased();
        }

        row.RefreshOverrides();
        RefreshOverrideState();
    }

    private void ReleaseAllTests()
    {
        _session.ReleaseAll();
        MarkAllReleased();
    }

    // ───────────────────────────── Building ─────────────────────────────

    private void Rebuild()
    {
        var project = _projects.Current;
        var health = PatchHealth.Analyze(project);
        _health = health;
        _fingerprint = PatchFingerprint(project);
        _checkedRevision = _projects.Revision;
        _rowsStale = false;
        BuildFixtureWarnings(health);
        var selectedId = SelectedFixture?.FixtureId;

        var ordered = new List<FixtureHealth>(health.Fixtures);
        ordered.Sort((a, b) =>
        {
            var c = a.Fixture.Universe.CompareTo(b.Fixture.Universe);
            if (c == 0)
            {
                c = a.Fixture.StartAddress.CompareTo(b.Fixture.StartAddress);
            }

            return c != 0 ? c : string.Compare(a.Fixture.Name, b.Fixture.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var old in Fixtures)
        {
            old.IsCurrent = false;
        }

        Fixtures.Clear();
        TestFixtureRowViewModel? reselect = null;
        foreach (var entry in ordered)
        {
            var row = CreateRow(entry);
            Fixtures.Add(row);
            if (row.FixtureId == selectedId)
            {
                reselect = row;
            }
        }

        UpdateUniverses(project);

        OnPropertyChanged(nameof(HasNoFixtures));
        ApplyFilter();
        SelectedFixture = reselect;
        RefreshMonitorPatch();
        RefreshOverrideState();
        PollOnce();
    }

    private TestFixtureRowViewModel CreateRow(FixtureHealth health)
    {
        var fixture = health.Fixture;
        var row = new TestFixtureRowViewModel(health, Detail(health), OnRowRelease);
        if (health.Profile is not { } profile || health.Mode is not { } mode)
        {
            return row;
        }

        for (var offset = 0; offset < mode.Channels.Count; offset++)
        {
            var key = mode.Channels[offset];
            var definition = profile.FindChannel(key);
            var address = fixture.StartAddress + offset;
            var available = definition is not null
                && _engine.TryResolveChannel(fixture.Id, key, out var patched)
                && patched.Universe == fixture.Universe
                && patched.Address == address;
            var channel = new TestChannelViewModel(
                row,
                definition ?? new ChannelDefinition { Key = key, Name = key, Type = ChannelType.Generic },
                offset + 1,
                fixture.Universe,
                address,
                available,
                definition is null,
                OnChannelSet,
                OnChannelRelease);
            if (available && _session.TryGetValue(fixture.Id, key, out var value))
            {
                channel.RestoreOverride(value);
            }

            row.Channels.Add(channel);
        }

        row.RefreshOverrides();
        return row;
    }

    private static string Detail(FixtureHealth health)
    {
        var fixture = health.Fixture;
        if (health.Profile is not { } profile)
        {
            return $"U{fixture.Universe} · {fixture.StartAddress} · missing profile \"{fixture.ProfileId}\"";
        }

        if (health.Mode is not { } mode)
        {
            return $"U{fixture.Universe} · {fixture.StartAddress} · {profile.DisplayName} · missing mode \"{fixture.ModeName}\"";
        }

        var range = mode.ChannelCount <= 1
            ? $"{fixture.StartAddress}"
            : $"{fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, mode.ChannelCount)}";
        return $"U{fixture.Universe} · {range} · {profile.DisplayName} · {mode.Name}";
    }

    /// <summary>
    /// Hash of what the rows show about the patch (lights, addresses, profiles, modes, names, universes); a change means
    /// the rows must be rebuilt. Computed without allocating (it used to build a string of every light on each visit).
    /// </summary>
    private static int PatchFingerprint(Project project)
    {
        var hash = default(HashCode);
        hash.Add(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            hash.Add(fixture.Id);
            hash.Add(fixture.Name, StringComparer.Ordinal);
            hash.Add(fixture.Universe);
            hash.Add(fixture.StartAddress);
            hash.Add(fixture.ProfileId, StringComparer.Ordinal);
            hash.Add(fixture.ModeName, StringComparer.Ordinal);
            hash.Add(fixture.Enabled);
            if (FindProfile(project, fixture.ProfileId) is not { } profile)
            {
                continue;
            }

            // Profile edits replace the profile object: its identity and the mode's channel keys catch them.
            hash.Add(RuntimeHelpers.GetHashCode(profile));
            if (profile.FindMode(fixture.ModeName) is { } mode)
            {
                hash.Add(mode.Channels.Count);
                foreach (var key in mode.Channels)
                {
                    hash.Add(key, StringComparer.Ordinal);
                }
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>Same match as <see cref="Project.FindProfile"/>, without a closure per call.</summary>
    private static FixtureProfile? FindProfile(Project project, string profileId)
    {
        foreach (var profile in project.Profiles)
        {
            if (string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        FilteredFixtures.Clear();
        foreach (var row in Fixtures)
        {
            if (row.Universe == SelectedUniverse && (search.Length == 0 || row.Matches(search)))
            {
                FilteredFixtures.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasNoMatches));
        PasteToAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(NoMatchesTitle));
        OnPropertyChanged(nameof(NoMatchesText));
    }

    /// <summary>
    /// Updates the universe list in place (a Clear would blank the bound ComboBox selection), keeping the selection when
    /// the universe still exists.
    /// </summary>
    private void UpdateUniverses(Project project)
    {
        // A light patched to a universe the project does not list still needs a desk page.
        var set = new SortedSet<int>();
        foreach (var universe in project.Universes)
        {
            if (DmxAddress.IsValidUniverse(universe))
            {
                set.Add(universe);
            }
        }

        foreach (var fixture in project.Fixtures)
        {
            if (DmxAddress.IsValidUniverse(fixture.Universe))
            {
                set.Add(fixture.Universe);
            }
        }

        var wanted = new List<int>(set);

        for (var i = Universes.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Universes[i]))
            {
                Universes.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= Universes.Count || Universes[i] != wanted[i])
            {
                if (Universes.Contains(wanted[i]))
                {
                    Universes.Remove(wanted[i]);
                }

                Universes.Insert(i, wanted[i]);
            }
        }

        if (!Universes.Contains(SelectedUniverse))
        {
            SelectedUniverse = Universes.Count > 0 ? Universes[0] : 1;
        }
        else
        {
            // Re-raise so a ComboBox that lost its selection while the list changed shows it again.
            OnPropertyChanged(nameof(SelectedUniverse));
        }
    }

    // ───────────────────────────── Override state ─────────────────────────────

    private void MarkAllReleased()
    {
        foreach (var row in Fixtures)
        {
            foreach (var channel in row.Channels)
            {
                channel.MarkReleased();
            }

            row.RefreshOverrides();
        }

        RefreshOverrideState();
    }

    /// <summary>After Reapply without a rebuild: channels the session dropped are no longer under test.</summary>
    private void SyncOverrideFlags()
    {
        foreach (var row in Fixtures)
        {
            foreach (var channel in row.Channels)
            {
                if (channel.IsOverridden && !_session.IsOverridden(channel.FixtureId, channel.Key))
                {
                    channel.MarkReleased();
                }
            }

            row.RefreshOverrides();
        }

        RefreshOverrideState();
    }

    private void RefreshOverrideState()
    {
        OnPropertyChanged(nameof(OverriddenCount));
        OnPropertyChanged(nameof(HasOverrides));
        OnPropertyChanged(nameof(OverrideSummary));
        OnPropertyChanged(nameof(SaveAsFunctionToolTip));
        ReleaseAllTestsCommand.NotifyCanExecuteChanged();
        SaveAsFunctionCommand.NotifyCanExecuteChanged();
    }

    // ───────────────────────────── Polling ─────────────────────────────

    private void StartPolling()
    {
        if (_poll is not null)
        {
            return;
        }

        _poll = new CancellationTokenSource();
        _ = PollLoopAsync(_poll.Token);
    }

    private void StopPolling()
    {
        _poll?.Cancel();
        _poll?.Dispose();
        _poll = null;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(token))
            {
                PollTick();
            }
        }
        catch (OperationCanceledException)
        {
            // Page left.
        }
    }

    /// <summary>One poll; a failure is logged and counted, polling continues (safety rule 10: shown, not just logged).</summary>
    private void PollTick()
    {
        try
        {
            PollOnce();
            if (_pollFailures > 0)
            {
                _pollFailures = 0;
                _pollError = null;
                RefreshWarnings();
            }
        }
        catch (Exception ex)
        {
            if (_pollFailures++ == 0)
            {
                _logger.LogError(ex, "Test page polling failed");
            }

            if (_pollFailures >= PollFailuresBeforeWarning && _pollError is null)
            {
                _pollError = ex.Message;
                RefreshWarnings();
            }
        }

        if (++_pollCount % WarningEveryPolls == 0)
        {
            try
            {
                RefreshWarnings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test page warnings failed");
            }
        }
    }

    private void PollOnce()
    {
        // An engine-wide clear that bypassed this page (any caller of DmxEngine.ReleaseAll/ClearOverrides/LoadProject).
        if (_session.Sync() > 0)
        {
            SyncOverrideFlags();
        }

        var snapshot = _engine.GetSnapshot();
        if (snapshot is null)
        {
            return;
        }

        var grandMaster = snapshot.GrandMaster;

        // The Virtual Console master moves the same Grand Master.
        SetGrandMasterShown(Math.Round(_engine.GrandMaster * 100));
        var blackout = snapshot.Blackout;
        var allowStrobe = _engine.AllowStrobe;
        foreach (var row in Fixtures)
        {
            foreach (var channel in row.Channels)
            {
                if (!channel.IsAvailable)
                {
                    continue;
                }

                var output = snapshot.GetValue(channel.Universe, channel.DmxAddress);
                var strobeBlocked = !allowStrobe && channel.Type == ChannelType.Strobe;
                string? reason = null;
                if (channel.IsOverridden)
                {
                    var intensity = channel.Type.IsIntensity();
                    reason = _session.IsShadowed(channel.FixtureId, channel.Key) ? "shared address"
                        : intensity && blackout ? "Blackout"
                        : intensity && grandMaster < 1 ? "Grand Master"
                        : strobeBlocked ? "strobe not allowed"
                        : null;
                }

                channel.UpdateLive(output, strobeBlocked, reason);
            }
        }

        PollMonitor(snapshot);
    }

    /// <summary>
    /// Updates the 512 monitor cells. Skipped while the monitor is hidden (the default): the cells would still push
    /// every change through their bindings; showing the monitor refreshes it at once.
    /// </summary>
    private void PollMonitor(EngineSnapshot? snapshot)
    {
        if (snapshot is null || !IsMonitorVisible)
        {
            return;
        }

        var universe = SelectedUniverse;
        Array.Clear(_monitorOverridden);
        foreach (var entry in _session.Overrides)
        {
            if (entry.Universe == universe && DmxAddress.IsValid(entry.Address))
            {
                _monitorOverridden[DmxAddress.ToIndex(entry.Address)] = true;
            }
        }

        var data = snapshot.GetUniverse(universe);
        for (var i = 0; i < MonitorCells.Count; i++)
        {
            var cell = MonitorCells[i];
            cell.SetValue(data.IsEmpty ? (byte)0 : data[i]);
            cell.SetOverridden(_monitorOverridden[i]);
        }
    }

    /// <summary>Which light/channel sits at each address of the shown universe, and the selected light's footprint.</summary>
    private void RefreshMonitorPatch()
    {
        var universe = SelectedUniverse;
        var labels = new string?[DmxAddress.UniverseSize];
        var selected = new bool[DmxAddress.UniverseSize];
        foreach (var row in Fixtures)
        {
            if (row.Universe != universe)
            {
                continue;
            }

            foreach (var channel in row.Channels)
            {
                if (!channel.IsAvailable)
                {
                    continue;
                }

                var index = DmxAddress.ToIndex(channel.DmxAddress);
                var label = $"{row.Name} · {channel.Name}";
                labels[index] = labels[index] is { } existing ? $"{existing} / {label}" : label;
                if (row.IsSelected)
                {
                    selected[index] = true;
                }
            }
        }

        for (var i = 0; i < MonitorCells.Count; i++)
        {
            MonitorCells[i].SetPatch(universe, labels[i]);
            MonitorCells[i].SetSelected(selected[i]);
        }
    }

    // ───────────────────────────── Warnings ─────────────────────────────

    /// <summary>
    /// Re-evaluates the warnings strip (~3 Hz while visible). Allocation-free when nothing it shows changed: the inputs
    /// are compared first, and the light warnings are only rebuilt with the rows.
    /// </summary>
    private void RefreshWarnings()
    {
        var inputs = new WarningInputs(
            _pollError,
            _inputOutput.State,
            _inputOutput.State == OutputState.Error ? _inputOutput.StatusMessage ?? _inputOutput.StatusText : null,
            _engine.Blackout,
            GrandMasterPercentOf(_engine.GrandMaster),
            _engine.AllowStrobe,
            _health);
        if (_hasWarningInputs && inputs == _warningInputs)
        {
            return;
        }

        _warningInputs = inputs;
        _hasWarningInputs = true;

        var list = BuildWarnings(inputs);
        var same = list.Count == Warnings.Count;
        for (var i = 0; same && i < list.Count; i++)
        {
            same = list[i].SameAs(Warnings[i]);
        }

        if (same)
        {
            return;
        }

        Warnings.Clear();
        foreach (var warning in list)
        {
            Warnings.Add(warning);
        }

        OnPropertyChanged(nameof(HasWarnings));
    }

    private List<TestWarningViewModel> BuildWarnings(WarningInputs inputs)
    {
        var list = new List<TestWarningViewModel>();
        if (inputs.PollError is { } pollError)
        {
            list.Add(new($"Live values stopped updating: {pollError}", "The faders and the DMX monitor may not show what is sent.", isError: true));
        }

        switch (inputs.OutputState)
        {
            case OutputState.Disabled:
                list.Add(new(
                    "Output is disabled — nothing reaches the lights. Enable it in Input / Output (Ctrl+6).",
                    "The faders and the monitor show what would be sent. DMX output always starts disabled.",
                    isError: false));
                break;
            case OutputState.Error:
                list.Add(new(
                    $"Output error: {inputs.OutputError}",
                    "Open Input / Output (Ctrl+6) to fix the interface or network settings.",
                    isError: true));
                break;
        }

        if (inputs.Blackout)
        {
            list.Add(new("Blackout is on — colours and dimmers are sent as 0.", "Turn Blackout off to see test values on intensity channels.", isError: false));
        }

        if (inputs.GrandMasterPercent < 100)
        {
            list.Add(new(
                $"Grand Master is at {inputs.GrandMasterPercent} % — colours and dimmers are sent lower than the faders show.",
                "Grand Master scales intensity channels only.",
                isError: false));
        }

        if (inputs.Health is { AnyStrobeChannel: true } && !inputs.AllowStrobe)
        {
            list.Add(new(
                "Strobe is blocked by the safety setting — strobe channels are sent at their safe value.",
                "Allow strobe in Settings to test strobe channels. Check the light's manual first.",
                isError: false));
        }

        list.AddRange(_fixtureWarnings);
        return list;
    }

    /// <summary>
    /// Light warnings (missing profile, past 512, overlap, disabled) for the rows just built. Owner decision 2026-10-22:
    /// the Test page no longer warns about unverified profiles.
    /// </summary>
    private void BuildFixtureWarnings(PatchHealth health)
    {
        _fixtureWarnings.Clear();
        AddFixtureWarning(_fixtureWarnings, health, static h => h.IsMissingProfile || h.IsMissingMode, "light has a missing profile or mode", "lights have a missing profile or mode", "They have no channels to test. Fix them in Fixtures.", isError: true);
        AddFixtureWarning(_fixtureWarnings, health, static h => h.ExceedsUniverse || h.HasInvalidUniverse, "light does not fit in its universe", "lights do not fit in their universe", "Channels past address 512 are not sent. Move them to a lower start address.", isError: true);
        AddFixtureWarning(_fixtureWarnings, health, static h => h.HasOverlap, "light shares addresses with another light", "lights share addresses with other lights", "Overlapping lights receive the same values on the shared channels.", isError: false);
        AddFixtureWarning(_fixtureWarnings, health, static h => h.IsDisabled, "light is disabled", "lights are disabled", "Disabled lights send nothing.", isError: false);

        // The strip compares its inputs by health reference; force one re-evaluation for the new rows.
        _hasWarningInputs = false;
    }

    /// <summary>Whole percent for the warning; anything below full stays below 100 so the warning still shows.</summary>
    private static int GrandMasterPercentOf(double grandMaster) =>
        grandMaster >= 1 ? 100 : (int)Math.Min(99, Math.Round(Math.Max(0, grandMaster) * 100));

    /// <summary>Everything the warnings strip is built from (compared by value to skip needless rebuilds).</summary>
    private readonly record struct WarningInputs(
        string? PollError,
        OutputState OutputState,
        string? OutputError,
        bool Blackout,
        int GrandMasterPercent,
        bool AllowStrobe,
        PatchHealth? Health);

    private static void AddFixtureWarning(
        List<TestWarningViewModel> list,
        PatchHealth health,
        Func<FixtureHealth, bool> predicate,
        string singular,
        string plural,
        string hint,
        bool isError)
    {
        var names = new List<string>();
        foreach (var entry in health.Fixtures)
        {
            if (predicate(entry))
            {
                names.Add(string.IsNullOrWhiteSpace(entry.Fixture.Name) ? "(unnamed light)" : entry.Fixture.Name);
            }
        }

        if (names.Count == 0)
        {
            return;
        }

        const int MaxNames = 10;
        var shown = string.Join(", ", names.Take(MaxNames)) + (names.Count > MaxNames ? $" and {names.Count - MaxNames} more" : string.Empty);
        list.Add(new($"{names.Count} {(names.Count == 1 ? singular : plural)}", $"{hint}{Environment.NewLine}{shown}", isError));
    }

    private void OnUiThread(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }
}
