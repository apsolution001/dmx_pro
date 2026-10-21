using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using DmxPro.App.Services;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Scene editor below the Functions page's function tree (QLC+-style): a toolbar with the name and the fade times,
/// and two tabs — <see cref="General"/> (which lights the scene uses) and <see cref="Channels"/> (a 0–255 fader per
/// channel of the selected lights). Edits a draft copy; nothing reaches the project (or the lights) until
/// <see cref="FunctionEditorViewModel.Save"/>. Every write is resolved through each fixture's profile mode (by
/// <see cref="ChannelType"/> or channel key, never by offset).
/// </summary>
public sealed class SceneEditorViewModel : FunctionEditorViewModel
{
    /// <summary>
    /// Channel types a newly added light copies from the lights already in the scene, so it matches them (dimmer
    /// full and colours off when the scene is empty).
    /// </summary>
    private static readonly ChannelType[] SeededTypes =
    [
        ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV, ChannelType.Dimmer,
    ];

    private readonly Project _project;
    private readonly Dictionary<Guid, SceneFixtureTabViewModel> _banks = [];
    private readonly Dictionary<Guid, SceneFixtureChoice> _fixtureChoices = [];
    private readonly Dictionary<Guid, int> _patchIndex = [];
    private string _bankSignature = string.Empty;
    private SceneFunction _draft = new();
    private SceneEditorTab? _selectedTab;
    private double _fadeInSeconds;
    private double _fadeOutSeconds;

    public SceneEditorViewModel(Project project, SceneFunction scene, LightClipboardService clipboard)
        : base(project, scene)
    {
        _project = project;

        General = new SceneGeneralTabViewModel(this);
        Channels = new SceneChannelsTabViewModel(SetChannelTypeOnSelected, clipboard);
        Tabs = [General, Channels];
        SelectedTab = General;

        BuildChoices();
        LoadFromOriginal();

        // A scene with lights opens on its channels (every channel of every light); an empty one on General,
        // where lights are added.
        if (Channels.Fixtures.Count > 0)
        {
            SelectedTab = Channels;
        }
    }

    /// <summary>The selected tab (General or Channels).</summary>
    public SceneEditorTab? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    /// <summary>Always [<see cref="General"/>, <see cref="Channels"/>].</summary>
    public ObservableCollection<SceneEditorTab> Tabs { get; }

    public SceneGeneralTabViewModel General { get; }

    public SceneChannelsTabViewModel Channels { get; }

    /// <summary>The project, for the General tab's Choose lights dialog.</summary>
    internal Project PatchProject => _project;

    /// <summary>
    /// Fade-in time typed in the editor toolbar ("0.5"). Numbers are rounded to milliseconds and clamped to
    /// 0–<see cref="FunctionEditing.MaxSeconds"/>; anything else reverts to the stored value.
    /// </summary>
    public string FadeInText
    {
        get => StepTimingRowViewModel.Format(_fadeInSeconds);
        set => SetSeconds(ref _fadeInSeconds, value);
    }

    /// <summary>Fade-out time typed in the editor toolbar; same rules as <see cref="FadeInText"/>.</summary>
    public string FadeOutText
    {
        get => StepTimingRowViewModel.Format(_fadeOutSeconds);
        set => SetSeconds(ref _fadeOutSeconds, value);
    }

    private SceneFunction Scene => (SceneFunction)Target;

    public override void RefreshPatch()
    {
        BuildChoices();
        RefreshBanks();
        foreach (var (fixtureId, choice) in _fixtureChoices)
        {
            choice.SetInSceneSilently(_banks.ContainsKey(fixtureId));
        }

        foreach (var group in General.AvailableGroups)
        {
            group.SetInSceneSilently(group.FixtureIds.Count > 0 && group.FixtureIds.All(_banks.ContainsKey));
        }
    }

    /// <summary>
    /// Rebuilds the fader banks when a light in the scene was deleted, renamed, moved or re-profiled since they were
    /// built (deleted lights leave the draft). Untouched banks are kept, so the light selection survives unrelated edits.
    /// </summary>
    private void RefreshBanks()
    {
        var fixtures = new Dictionary<Guid, Fixture>(_project.Fixtures.Count);
        foreach (var fixture in _project.Fixtures)
        {
            fixtures.TryAdd(fixture.Id, fixture);
        }

        var current = _banks.Keys.Select(id => fixtures.GetValueOrDefault(id)).ToList();
        var signature = BankSignature(current);
        if (signature == _bankSignature)
        {
            return;
        }

        foreach (var missing in _banks.Keys.Where(id => !fixtures.ContainsKey(id)).ToList())
        {
            _draft.RemoveFixture(missing);
            _banks.Remove(missing);
            if (_fixtureChoices.TryGetValue(missing, out var choice))
            {
                choice.SetInSceneSilently(false);
            }
        }

        var values = _draft.IndexValues();
        var banks = new List<SceneFixtureTabViewModel>();
        foreach (var fixture in _project.Fixtures.Where(f => _banks.ContainsKey(f.Id)))
        {
            var bank = CreateBank(fixture, values);
            _banks[fixture.Id] = bank;
            banks.Add(bank);
        }

        Channels.Reset(banks);
        _bankSignature = BankSignature(banks.Select(b => b.Fixture).ToList());
        RefreshDerived();
    }

    /// <summary>What the banks show about their lights; a change means the banks must be rebuilt.</summary>
    private static string BankSignature(IReadOnlyList<Fixture?> fixtures) =>
        string.Join("|", fixtures.Select(f => f is null
            ? "missing"
            : $"{f.Id}:{f.Name}:{f.Universe}:{f.StartAddress}:{f.ProfileId}:{f.ModeName}"));

    /// <summary>(Re)builds the Add list: every patched fixture in patch order, then every group.</summary>
    private void BuildChoices()
    {
        _patchIndex.Clear();
        _fixtureChoices.Clear();
        General.AvailableFixtures.Clear();
        General.AvailableGroups.Clear();
        for (var i = 0; i < _project.Fixtures.Count; i++)
        {
            var fixture = _project.Fixtures[i];
            _patchIndex[fixture.Id] = i;
            var profile = _project.FindProfile(fixture.ProfileId);
            var profileName = profile is null ? "missing profile" : profile.DisplayName;
            var detail = $"{AddressRange(fixture)} · {profileName}";
            var choice = new SceneFixtureChoice(fixture.Id, fixture.Name, detail, false, [fixture.Id], OnChoiceToggled);
            _fixtureChoices[fixture.Id] = choice;
            General.AvailableFixtures.Add(choice);
        }

        foreach (var group in _project.Groups)
        {
            var members = group.FixtureIds.Where(_patchIndex.ContainsKey).ToList();
            var count = members.Count == 1 ? "1 fixture" : $"{members.Count} fixtures";
            General.AvailableGroups.Add(new SceneFixtureChoice(group.Id, group.Name, count, true, members, OnChoiceToggled));
        }

        General.ApplyAddSearch();
    }

    // ───────────────────────────── Called by the tabs ─────────────────────────────

    /// <summary>
    /// Adds or removes fixtures. A new fixture gets every channel at its profile default, then copies the colour and
    /// dimmer levels of the lights already in the scene (<see cref="SeededTypes"/>).
    /// </summary>
    internal void SetFixturesInScene(IReadOnlyList<Guid> fixtureIds, bool include)
    {
        var seed = include ? CurrentSeedLevels() : [];
        var changed = false;
        foreach (var fixtureId in fixtureIds)
        {
            if (_project.FindFixture(fixtureId) is not { } fixture)
            {
                continue;
            }

            if (include && !_banks.ContainsKey(fixtureId))
            {
                _draft.AddFixture(_project, fixture);
                for (var i = 0; i < SeededTypes.Length; i++)
                {
                    _draft.SetChannelType(_project, fixture, SeededTypes[i], seed[i]);
                }

                var bank = CreateBank(fixture);
                _banks[fixtureId] = bank;
                Channels.Insert(PatchPosition(fixtureId), bank);
                changed = true;
            }
            else if (!include && _banks.Remove(fixtureId, out var bank))
            {
                _draft.RemoveFixture(fixtureId);
                Channels.Remove(bank);
                changed = true;
            }

            if (_fixtureChoices.TryGetValue(fixtureId, out var choice))
            {
                choice.SetInSceneSilently(include);
            }
        }

        if (!changed)
        {
            return;
        }

        Channels.AfterFixturesChanged();
        RefreshDerived();
        MarkDirty();
    }

    /// <summary>Includes/excludes every channel of one fixture bank (the bank's Enable all / Disable all).</summary>
    internal void SetAllChannelsIncluded(SceneFixtureTabViewModel bank, bool included)
    {
        foreach (var fader in bank.Channels)
        {
            _draft.SetChannelIncluded(bank.FixtureId, fader.Channel, included, included ? fader.ByteValue : null);
            fader.SetIncludedSilently(included);
        }

        Channels.RefreshBulkValue();
        MarkDirty();
    }

    /// <summary>
    /// "Set on selected lights": sets every channel of <paramref name="type"/> (resolved through each profile mode)
    /// of every light selected in the Channels tab, including channels that were not in the scene yet.
    /// </summary>
    internal void SetChannelTypeOnSelected(ChannelType type, byte value)
    {
        var changed = false;
        foreach (var bank in Channels.SelectedBanks)
        {
            if (_draft.SetChannelType(_project, bank.Fixture, type, value) == 0)
            {
                continue;
            }

            changed = true;
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    fader.SetIncludedSilently(true);
                    fader.SetValueSilently(value);
                }
            }
        }

        if (changed)
        {
            MarkDirty();
        }
    }

    // ───────────────────────────── Draft handling ─────────────────────────────

    /// <summary>Writes the draft values and fades into the scene.</summary>
    protected override void ApplyDraft()
    {
        Scene.FadeInSeconds = _fadeInSeconds;
        Scene.FadeOutSeconds = _fadeOutSeconds;
        Scene.Values = _draft.Values.Select(v => v.Clone()).ToList();
    }

    protected override void LoadDraft()
    {
        _draft = (SceneFunction)Scene.CloneAsNew();
        _fadeInSeconds = FunctionEditing.Clamp(Scene.FadeInSeconds, 0, FunctionEditing.MaxSeconds);
        _fadeOutSeconds = FunctionEditing.Clamp(Scene.FadeOutSeconds, 0, FunctionEditing.MaxSeconds);
        OnPropertyChanged(nameof(FadeInText));
        OnPropertyChanged(nameof(FadeOutText));

        var targets = _draft.TargetFixtureIds().ToHashSet();
        var values = _draft.IndexValues();
        _banks.Clear();
        var banks = new List<SceneFixtureTabViewModel>();
        foreach (var fixture in _project.Fixtures)
        {
            var inScene = targets.Contains(fixture.Id);
            if (_fixtureChoices.TryGetValue(fixture.Id, out var choice))
            {
                choice.SetInSceneSilently(inScene);
            }

            if (inScene)
            {
                var bank = CreateBank(fixture, values);
                _banks[fixture.Id] = bank;
                banks.Add(bank);
            }
        }

        Channels.Reset(banks);
        _bankSignature = BankSignature(banks.Select(b => b.Fixture).ToList());
        RefreshDerived();
    }

    /// <summary>Stores typed seconds: clamped and rounded, or reverted when not a number. Always re-shows the box.</summary>
    private void SetSeconds(ref double field, string? text, [CallerMemberName] string? propertyName = null)
    {
        if (FunctionEditing.TryParseNumber(text, 0, FunctionEditing.MaxSeconds, out var seconds) && seconds != field)
        {
            field = seconds;
            MarkDirty();
        }

        OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// The level each <see cref="SeededTypes"/> entry gets on a newly added light: the first included channel of that
    /// type in the scene (patch order), else full dimmer and colours off.
    /// </summary>
    private byte[] CurrentSeedLevels()
    {
        var levels = new byte[SeededTypes.Length];
        for (var i = 0; i < SeededTypes.Length; i++)
        {
            byte? value = null;
            foreach (var bank in Channels.Fixtures)
            {
                value = _draft.GetIncludedChannelTypeValue(_project, bank.Fixture, SeededTypes[i]);
                if (value is not null)
                {
                    break;
                }
            }

            levels[i] = value ?? (SeededTypes[i] == ChannelType.Dimmer ? (byte)255 : (byte)0);
        }

        return levels;
    }

    /// <param name="fixture">The light.</param>
    /// <param name="values">The draft's values from <see cref="SceneEditing.IndexValues"/> when building many banks at
    /// once (a lookup per channel instead of a scan of the whole draft); null reads the draft directly.</param>
    private SceneFixtureTabViewModel CreateBank(Fixture fixture, IReadOnlyDictionary<(Guid FixtureId, string ChannelKey), byte>? values = null)
    {
        var profile = _project.FindProfile(fixture.ProfileId);
        var unverified = profile is { Verified: false };
        var channels = _project.ResolveFixtureChannels(fixture);
        var mode = profile is null ? "missing profile" : fixture.ModeName;
        var bank = new SceneFixtureTabViewModel(fixture, $"{AddressRange(fixture, channels.Count)} · {mode}", unverified, SetAllChannelsIncluded);
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            var stored = values is null
                ? _draft.GetValue(fixture.Id, channel.Key)
                : values.TryGetValue((fixture.Id, channel.Key), out var indexed) ? indexed : (byte?)null;
            bank.Channels.Add(new SceneChannelFaderViewModel(
                fixture.Id,
                channel,
                number: i + 1,
                dmxAddress: fixture.StartAddress + i,
                unverified,
                stored ?? channel.Default,
                stored is not null,
                OnFaderChanged));
        }

        return bank;
    }

    private void OnChoiceToggled(SceneFixtureChoice choice, bool isInScene) => SetFixturesInScene(choice.FixtureIds, isInScene);

    private void OnFaderChanged(SceneChannelFaderViewModel fader)
    {
        _draft.SetChannelIncluded(fader.FixtureId, fader.Channel, fader.IsIncluded, fader.IsIncluded ? fader.ByteValue : null);
        if (Channels.BulkType?.Type == fader.Type)
        {
            Channels.RefreshBulkValue();
        }

        MarkDirty();
    }

    /// <summary>Where a fixture's bank goes in the patch-ordered list of banks.</summary>
    private int PatchPosition(Guid fixtureId)
    {
        var target = _patchIndex.GetValueOrDefault(fixtureId, int.MaxValue);
        var position = 0;
        foreach (var bank in Channels.Fixtures)
        {
            if (_patchIndex.GetValueOrDefault(bank.FixtureId, int.MaxValue) < target)
            {
                position++;
            }
        }

        return position;
    }

    /// <summary>The General tab's light list, group ticks and the unverified-profile warning after the fixture set changed.</summary>
    private void RefreshDerived()
    {
        General.SetLights(Channels.Fixtures.Select(CreateLightRow).ToList());

        foreach (var group in General.AvailableGroups)
        {
            var all = group.FixtureIds.Count > 0;
            foreach (var id in group.FixtureIds)
            {
                if (!_banks.ContainsKey(id))
                {
                    all = false;
                    break;
                }
            }

            group.SetInSceneSilently(all);
        }

        var unverified = Channels.Fixtures
            .Where(b => b.ProfileUnverified)
            .Select(b => _project.FindProfile(b.Fixture.ProfileId)?.DisplayName ?? b.Fixture.ProfileId)
            .Distinct()
            .ToList();
        UnverifiedWarning = unverified.Count == 0
            ? null
            : $"Not verified on a real fixture: {string.Join(", ", unverified)}. Check the channel meanings before using this scene in a show.";
    }

    private SceneLightRow CreateLightRow(SceneFixtureTabViewModel bank)
    {
        var fixture = bank.Fixture;
        var profile = _project.FindProfile(fixture.ProfileId);
        return new SceneLightRow(
            fixture.Id,
            fixture.Name,
            profile?.DisplayName ?? $"Missing profile \"{fixture.ProfileId}\"",
            profile is null ? "–" : fixture.ModeName,
            AddressRange(fixture, bank.Channels.Count),
            bank.ProfileUnverified);
    }

    private string AddressRange(Fixture fixture) => AddressRange(fixture, _project.ResolveFixtureChannels(fixture).Count);

    private static string AddressRange(Fixture fixture, int channelCount) =>
        channelCount <= 1
            ? $"U{fixture.Universe} · {fixture.StartAddress}"
            : $"U{fixture.Universe} · {fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, channelCount)}";
}
