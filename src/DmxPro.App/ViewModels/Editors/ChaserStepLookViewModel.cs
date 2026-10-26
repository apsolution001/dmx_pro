using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// The step-look panel of the chaser editor: edits the look of ONE own-look step, which is a (draft) owned
/// <see cref="SceneFunction"/>. Ported from the Sequence editor's step panel:
/// <list type="bullet">
/// <item><b>Lights</b> of this step (<see cref="ChooseLightsCommand"/> opens the Choose lights dialog; All / None
/// selection). The step's lights are the fixtures the scene has values for; new lights start at profile defaults.</item>
/// <item><b>Colours</b> (<see cref="ValuesViewIndex"/> 0): colour presets, Off and Intensity / colour sliders for the
/// selected lights. A preset raises a dimmer that is at 0 to full so the colour shows (same as the Sequence editor).</item>
/// <item><b>All channels</b> (<see cref="ValuesViewIndex"/> 1): the scene editor's Channels view, reused unchanged.</item>
/// </list>
/// The panel writes straight into the scene it was given, so the host must pass a draft (never a library function or
/// the saved owned scene). Every change raises <see cref="Changed"/> so the host can mark itself dirty and refresh the
/// live preview. Every channel is resolved through the fixture's profile mode by <see cref="ChannelType"/>.
/// </summary>
public sealed partial class ChaserStepLookViewModel : ObservableObject
{
    /// <summary><see cref="ValuesViewIndex"/> of the simple Colours view.</summary>
    public const int ColoursView = 0;

    /// <summary><see cref="ValuesViewIndex"/> of the All channels view.</summary>
    public const int AllChannelsView = 1;

    /// <summary>Slider rows of the Colours view, in this order, when a selected light has the type.</summary>
    private static readonly ChannelType[] LevelTypes =
    [
        ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV,
    ];

    private readonly Project _project;
    private readonly AppSettingsService _settings;
    private SceneFunction _scene;

    /// <summary>0 = Colours (simple), 1 = All channels (every channel's fader).</summary>
    [ObservableProperty]
    private int _valuesViewIndex;

    /// <summary>Panel title, e.g. "Step 2 · Intro"; set by the host.</summary>
    [ObservableProperty]
    private string _title = "Step look";

    public ChaserStepLookViewModel(
        Project project,
        SceneFunction draftScene,
        AppSettingsService settings,
        LightClipboardService clipboard,
        IReadOnlyList<Guid>? defaultLightIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draftScene);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clipboard);
        _project = project;
        _settings = settings;
        _scene = draftScene;
        Channels = new SceneChannelsTabViewModel(SetChannelTypeOnSelected, clipboard);
        Channels.PropertyChanged += OnChannelsChanged;

        // Weak: the panel comes and goes with the editor, the settings service lives as long as the app.
        WeakEventManager<AppSettingsService, EventArgs>.AddHandler(settings, nameof(AppSettingsService.Changed), OnSettingsChanged);

        DefaultLightsApplied = ApplyDefaultLights(defaultLightIds);
        LoadValues(new HashSet<Guid>());
    }

    /// <summary>
    /// The step's look changed (values or lights). The host marks itself dirty and refreshes the preview. Not raised by
    /// the constructor; see <see cref="DefaultLightsApplied"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Opens the "Choose lights" dialog modally; returns true on OK. Set by the view (the editors are created without
    /// DI), or by tests. While null, <see cref="ChooseLightsCommand"/> does nothing.
    /// </summary>
    public Func<FixturePickerViewModel, bool>? ShowPicker { get; set; }

    /// <summary>The scene being edited (the step's draft look).</summary>
    public SceneFunction Scene => _scene;

    /// <summary>
    /// True when the last <see cref="SetScene"/> or the constructor filled an empty scene with the default lights (at
    /// profile defaults). The constructor cannot raise <see cref="Changed"/>, so the host checks this to count the
    /// new lights as an edit; <see cref="SetScene"/> also raises <see cref="Changed"/>.
    /// </summary>
    public bool DefaultLightsApplied { get; private set; }

    /// <summary>All channels view of the step (the scene editor's Channels tab, reused).</summary>
    public SceneChannelsTabViewModel Channels { get; }

    /// <summary>The step's lights for the Colours view, patch order (selection shared with <see cref="Channels"/>).</summary>
    public ObservableCollection<ChaserStepLookLightRowViewModel> Lights { get; } = [];

    /// <summary>Intensity and colour sliders for the selected lights (only the types they have).</summary>
    public ObservableCollection<ChaserStepLookLevelRowViewModel> Levels { get; } = [];

    /// <summary>The same one-click colours as the colour editors.</summary>
    public IReadOnlyList<SceneEditorColorPreset> Presets => SceneEditorColorPreset.All;

    /// <summary>The step's patched lights in patch order (for the host: "next step starts with these lights").</summary>
    public IReadOnlyList<Guid> LightIds => Lights.Select(l => l.FixtureId).ToList();

    public bool HasLights => Lights.Count > 0;

    public bool HasNoLights => Lights.Count == 0;

    public bool HasSelectedLights => Channels.SelectedCount > 0;

    public bool HasNoSelectedLights => Channels.SelectedCount == 0 && Lights.Count > 0;

    public bool HasLevels => Levels.Count > 0;

    /// <summary>"2 of 4 selected".</summary>
    public string SelectedLightsText => $"{Channels.SelectedCount} of {Lights.Count} selected";

    /// <summary>"4 lights" for the lights header.</summary>
    public string LightCountText => Lights.Count == 1 ? "1 light" : $"{Lights.Count} lights";

    /// <summary>
    /// A light of this step has a strobe channel while "Allow strobe" is off in Settings: the engine holds those
    /// channels at their safe value, so the panel says so (safety rule 8).
    /// </summary>
    public bool ShowStrobeBlockedHint => !_settings.Current.AllowStrobe && HasStrobeChannel();

    /// <summary>
    /// Switches the panel to another step's (draft) scene, keeping the light selection where those lights are in the
    /// new step too. An empty scene gets <paramref name="defaultLightIds"/> at profile defaults (that raises
    /// <see cref="Changed"/>).
    /// </summary>
    public void SetScene(SceneFunction scene, IReadOnlyList<Guid>? defaultLightIds = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var previouslySelected = SelectedFixtureIds();
        _scene = scene;
        DefaultLightsApplied = ApplyDefaultLights(defaultLightIds);
        LoadValues(previouslySelected);
        if (DefaultLightsApplied)
        {
            RaiseChanged();
        }
    }

    /// <summary>Rebuilds the lights and faders after the patch changed (renamed, re-addressed or deleted fixtures).</summary>
    public void RefreshPatch() => LoadValues(SelectedFixtureIds());

    // ───────────────────────────── Commands ─────────────────────────────

    /// <summary>
    /// Opens the "Choose lights" dialog with the step's lights picked; on OK newly picked lights join the step at
    /// profile defaults (and are selected, ready for a colour) and unpicked ones leave it.
    /// </summary>
    [RelayCommand]
    private void ChooseLights()
    {
        if (ShowPicker is not { } show)
        {
            return;
        }

        var current = LightIds;
        var picker = FixturePickerViewModel.FromProject(_project, current, "Choose lights for this step", ordered: false);
        if (!show(picker))
        {
            return;
        }

        var result = picker.Result;
        var added = FixtureSelection.Added(current, result);
        var removed = FixtureSelection.Added(result, current);
        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        foreach (var id in removed)
        {
            _scene.RemoveFixture(id);
        }

        foreach (var id in added)
        {
            if (_project.FindFixture(id) is { } fixture)
            {
                _scene.AddFixture(_project, fixture);
            }
        }

        var keep = SelectedFixtureIds();
        keep.UnionWith(added);
        LoadValues(keep);
        RaiseChanged();
    }

    /// <summary>Sets every selected light of the step to the preset colour.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLights))]
    private void ApplyPreset(SceneEditorColorPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        EditSelectedLights(fixture => ChaserStepLookEditing.ApplyColor(_scene, _project, fixture, preset));
    }

    /// <summary>Turns every selected light of the step off (colours and intensity at 0).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLights))]
    private void LightsOff() => EditSelectedLights(fixture => ChaserStepLookEditing.SetOff(_scene, _project, fixture));

    [RelayCommand]
    private void SelectAllLights() => Channels.SelectAllCommand.Execute(null);

    [RelayCommand]
    private void SelectNoLights() => Channels.ClearSelectionCommand.Execute(null);

    // ───────────────────────────── Loading ─────────────────────────────

    /// <summary>Fills an empty scene with the default lights (profile defaults). Returns true when something was added.</summary>
    private bool ApplyDefaultLights(IReadOnlyList<Guid>? defaultLightIds)
    {
        if (defaultLightIds is null || defaultLightIds.Count == 0 || _scene.Values.Count > 0)
        {
            return false;
        }

        var added = false;
        foreach (var id in defaultLightIds)
        {
            if (_project.FindFixture(id) is { } fixture)
            {
                _scene.AddFixture(_project, fixture);
                added = true;
            }
        }

        return added;
    }

    private HashSet<Guid> SelectedFixtureIds() =>
        Channels.Fixtures.Where(b => b.IsSelected).Select(b => b.FixtureId).ToHashSet();

    /// <summary>
    /// Shows the scene's lights (Colours view) and fader banks (All channels). Lights in <paramref name="keepSelected"/>
    /// stay selected; when none of them is in this step, every light starts selected.
    /// </summary>
    private void LoadValues(IReadOnlySet<Guid> keepSelected)
    {
        var values = _scene.IndexValues();
        var inScene = new HashSet<Guid>();
        foreach (var value in _scene.Values)
        {
            inScene.Add(value.FixtureId);
        }

        var banks = new List<SceneFixtureTabViewModel>();
        var seen = new HashSet<Guid>();
        foreach (var fixture in _project.Fixtures)
        {
            if (inScene.Contains(fixture.Id) && seen.Add(fixture.Id))
            {
                banks.Add(CreateBank(fixture, values));
            }
        }

        Lights.Clear();
        Channels.Reset(banks);
        if (banks.Any(b => keepSelected.Contains(b.FixtureId)))
        {
            foreach (var bank in banks)
            {
                bank.SetSelectedSilently(keepSelected.Contains(bank.FixtureId));
            }

            Channels.AfterFixturesChanged();
        }

        foreach (var bank in banks)
        {
            var color = values.PreviewColor(_project, bank.Fixture);
            Lights.Add(new ChaserStepLookLightRowViewModel(
                bank,
                ChaserStepLookEditing.Hex(color),
                ChaserStepLookEditing.SwatchToolTip(bank.Header, color)));
        }

        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(LightIds));
        OnPropertyChanged(nameof(HasLights));
        OnPropertyChanged(nameof(HasNoLights));
        OnPropertyChanged(nameof(LightCountText));
        OnPropertyChanged(nameof(ShowStrobeBlockedHint));
        AfterLightSelectionChanged();
    }

    private SceneFixtureTabViewModel CreateBank(Fixture fixture, IReadOnlyDictionary<(Guid FixtureId, string ChannelKey), byte> values)
    {
        var profile = _project.FindProfile(fixture.ProfileId);
        var channels = _project.ResolveFixtureChannels(fixture);
        var mode = profile is null ? "missing profile" : fixture.ModeName;
        var range = channels.Count <= 1
            ? $"U{fixture.Universe} · {fixture.StartAddress}"
            : $"U{fixture.Universe} · {fixture.StartAddress}–{DmxAddress.EndAddress(fixture.StartAddress, channels.Count)}";
        var bank = new SceneFixtureTabViewModel(fixture, $"{range} · {mode}", SetAllChannelsIncluded);
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            byte? stored = values.TryGetValue((fixture.Id, channel.Key), out var value) ? value : null;
            bank.Channels.Add(new SceneChannelFaderViewModel(
                fixture.Id,
                channel,
                number: i + 1,
                dmxAddress: fixture.StartAddress + i,
                stored ?? channel.Default,
                stored is not null,
                OnFaderChanged));
        }

        return bank;
    }

    // ───────────────────────────── Selection and sliders ─────────────────────────────

    private void OnChannelsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Raised once per selection rebuild in the Channels view model.
        if (e.PropertyName == nameof(SceneChannelsTabViewModel.SelectionSummary))
        {
            AfterLightSelectionChanged();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ShowStrobeBlockedHint));

    private void AfterLightSelectionChanged()
    {
        RebuildLevels();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        LightsOffCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedLights));
        OnPropertyChanged(nameof(HasNoSelectedLights));
        OnPropertyChanged(nameof(SelectedLightsText));
    }

    /// <summary>One slider per level type a selected light has, showing the first selected light's level.</summary>
    private void RebuildLevels()
    {
        Levels.Clear();
        foreach (var type in LevelTypes)
        {
            if (FirstSelectedLevel(type) is { } level)
            {
                Levels.Add(new ChaserStepLookLevelRowViewModel(type, level, SetLevelOnSelected));
            }
        }

        OnPropertyChanged(nameof(HasLevels));
    }

    private void RefreshLevelValues()
    {
        foreach (var row in Levels)
        {
            if (FirstSelectedLevel(row.Type) is { } level)
            {
                row.SetValueSilently(level);
            }
        }
    }

    private int? FirstSelectedLevel(ChannelType type)
    {
        foreach (var bank in Channels.SelectedBanks)
        {
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    return fader.ByteValue;
                }
            }
        }

        return null;
    }

    private bool HasStrobeChannel()
    {
        foreach (var bank in Channels.Fixtures)
        {
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == ChannelType.Strobe)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ───────────────────────────── Edits ─────────────────────────────

    private void SetLevelOnSelected(ChannelType type, byte value)
    {
        SetChannelTypeOnSelected(type, value);
        Channels.RefreshBulkValue();
    }

    /// <summary>Runs an edit on every selected light of the step, then syncs faders, swatches and sliders.</summary>
    private void EditSelectedLights(Func<Fixture, bool> edit)
    {
        var changed = new List<Guid>();
        foreach (var bank in Channels.SelectedBanks)
        {
            if (edit(bank.Fixture))
            {
                SyncBank(bank);
                changed.Add(bank.FixtureId);
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        Channels.RefreshBulkValue();
        AfterValuesChanged(changed);
    }

    /// <summary>Re-reads one bank's faders from the scene (after an edit that bypassed them).</summary>
    private void SyncBank(SceneFixtureTabViewModel bank)
    {
        foreach (var fader in bank.Channels)
        {
            var stored = _scene.GetValue(bank.FixtureId, fader.Channel.Key);
            fader.SetIncludedSilently(stored is not null);
            if (stored is { } value)
            {
                fader.SetValueSilently(value);
            }
        }
    }

    private void OnFaderChanged(SceneChannelFaderViewModel fader)
    {
        _scene.SetChannelIncluded(fader.FixtureId, fader.Channel, fader.IsIncluded, fader.IsIncluded ? fader.ByteValue : null);
        if (Channels.BulkType?.Type == fader.Type)
        {
            Channels.RefreshBulkValue();
        }

        AfterValuesChanged([fader.FixtureId]);
    }

    private void SetAllChannelsIncluded(SceneFixtureTabViewModel bank, bool included)
    {
        foreach (var fader in bank.Channels)
        {
            _scene.SetChannelIncluded(bank.FixtureId, fader.Channel, included, included ? fader.ByteValue : null);
            fader.SetIncludedSilently(included);
        }

        Channels.RefreshBulkValue();
        AfterValuesChanged([bank.FixtureId]);
    }

    /// <summary>"Set on selected lights" (All channels view) and the Colours sliders: one channel type on every selected light.</summary>
    private void SetChannelTypeOnSelected(ChannelType type, byte value)
    {
        var changed = new List<Guid>();
        foreach (var bank in Channels.SelectedBanks)
        {
            if (_scene.SetChannelType(_project, bank.Fixture, type, value) == 0)
            {
                continue;
            }

            changed.Add(bank.FixtureId);
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    fader.SetIncludedSilently(true);
                    fader.SetValueSilently(value);
                }
            }
        }

        if (changed.Count > 0)
        {
            AfterValuesChanged(changed);
        }
    }

    /// <summary>The step's values changed for these lights: update their swatches and the sliders, tell the host.</summary>
    private void AfterValuesChanged(IReadOnlyCollection<Guid> fixtureIds)
    {
        foreach (var light in Lights)
        {
            if (fixtureIds.Contains(light.FixtureId))
            {
                var color = _scene.PreviewColor(_project, light.Bank.Fixture);
                light.SetSwatch(ChaserStepLookEditing.Hex(color), ChaserStepLookEditing.SwatchToolTip(light.Name, color));
            }
        }

        RefreshLevelValues();
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
