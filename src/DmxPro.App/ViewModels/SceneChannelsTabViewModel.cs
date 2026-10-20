using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>
/// The scene editor's "Channels" tab: a searchable list of the fixtures in the scene (patch order) and the fader
/// banks of the selected ones: every channel of each light's profile mode, 0-255. All lights start selected.
/// Built to stay cheap with hundreds of fixtures: a keystroke only re-filters the list, and a selection change only
/// rebuilds <see cref="SelectedBanks"/> once. "Set on selected lights" writes one channel type (resolved per profile)
/// on every selected light at once. Copy / Paste move a light's values onto other lights (QLC+ "copy current values"),
/// matched by the profile (<see cref="CopiedLightValues"/>), through the app-wide <see cref="LightClipboardService"/>, so
/// a light copied on the Test page can be pasted here too.
/// </summary>
public sealed partial class SceneChannelsTabViewModel : SceneEditorTab
{
    private readonly Action<ChannelType, byte> _setTypeOnSelected;
    private readonly LightClipboardService _clipboard;
    private bool _batch;
    private bool _silentBulk;

    [ObservableProperty]
    private SceneChannelTypeOption? _bulkType;

    [ObservableProperty]
    private int _bulkValue;

    internal SceneChannelsTabViewModel(Action<ChannelType, byte> setTypeOnSelected, LightClipboardService clipboard)
    {
        _setTypeOnSelected = setTypeOnSelected;
        _clipboard = clipboard;

        // Weak: editors come and go, the clipboard lives as long as the app.
        WeakEventManager<LightClipboardService, EventArgs>.AddHandler(clipboard, nameof(LightClipboardService.Changed), OnClipboardChanged);
    }

    /// <summary>Something was copied (here or on the Test page): Paste is possible.</summary>
    public bool HasCopiedValues => _clipboard.HasValues;

    /// <summary>"Copied: PAR 1" for the toolbar, or null.</summary>
    public string? CopiedText => _clipboard.Current is { } copied ? $"Copied: {copied.SourceName}" : null;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSelection))]
    private int _selectedCount;

    public override string Header => "Channels";

    public override string IconKind => "Channel";

    /// <summary>Every fixture in the scene, in patch order.</summary>
    public ObservableCollection<SceneFixtureTabViewModel> Fixtures { get; } = [];

    /// <summary><see cref="Fixtures"/> matching <see cref="SearchText"/> (name, universe, address or mode).</summary>
    public ObservableCollection<SceneFixtureTabViewModel> FilteredFixtures { get; } = [];

    /// <summary>The selected fixtures, in patch order: their fader banks are shown.</summary>
    public ObservableCollection<SceneFixtureTabViewModel> SelectedBanks { get; } = [];

    public bool HasNoSelection => SelectedCount == 0;

    public bool HasSelection => SelectedCount > 0;

    /// <summary>Channel types the selected lights have (Unknown and Fine excluded), for "Set on selected lights".</summary>
    public ObservableCollection<SceneChannelTypeOption> BulkTypes { get; } = [];

    public bool HasBulkTypes => BulkTypes.Count > 0;

    /// <summary>"3 lights · 24 channels": what the fader area shows.</summary>
    public string SelectionSummary
    {
        get
        {
            var channels = 0;
            foreach (var bank in SelectedBanks)
            {
                channels += bank.Channels.Count;
            }

            var lights = SelectedBanks.Count == 1 ? "1 light" : $"{SelectedBanks.Count} lights";
            return channels == 1 ? $"{lights} · 1 channel" : $"{lights} · {channels} channels";
        }
    }

    public bool HasNoFixtures => Fixtures.Count == 0;

    /// <summary>Replaces the fixture list (editor open / revert). Selects every fixture, so all channels show.</summary>
    internal void Reset(IReadOnlyList<SceneFixtureTabViewModel> banks)
    {
        Fixtures.Clear();
        SelectedBanks.Clear();
        foreach (var bank in banks)
        {
            bank.AttachSelection(OnBankSelectionChanged);
            bank.SetSelectedSilently(true);
            Fixtures.Add(bank);
        }

        AfterFixturesChanged();
    }

    /// <summary>Inserts a fixture bank at its patch-order position, selected so its faders show right away.</summary>
    internal void Insert(int index, SceneFixtureTabViewModel bank)
    {
        bank.AttachSelection(OnBankSelectionChanged);
        bank.SetSelectedSilently(true);
        Fixtures.Insert(Math.Clamp(index, 0, Fixtures.Count), bank);
    }

    /// <summary>
    /// Faders moved from elsewhere (a fader, a colour row, a bulk set): keep the "Set on selected lights" value in
    /// step with the first selected channel of its type.
    /// </summary>
    internal void RefreshBulkValue()
    {
        if (BulkType is { } option)
        {
            SetBulkValueSilently(FirstSelectedValue(option.Type));
        }
    }

    internal void Remove(SceneFixtureTabViewModel bank)
    {
        bank.SetSelectedSilently(false);
        Fixtures.Remove(bank);
    }

    /// <summary>Call once after a batch of <see cref="Insert"/>/<see cref="Remove"/>: refilters and keeps a selection.</summary>
    internal void AfterFixturesChanged()
    {
        var anySelected = false;
        foreach (var bank in Fixtures)
        {
            if (bank.IsSelected)
            {
                anySelected = true;
                break;
            }
        }

        if (!anySelected && Fixtures.Count > 0)
        {
            Fixtures[0].SetSelectedSilently(true);
        }

        ApplyFilter();
        RebuildSelection();
        OnPropertyChanged(nameof(HasNoFixtures));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnBulkTypeChanged(SceneChannelTypeOption? value)
    {
        if (value is not null)
        {
            SetBulkValueSilently(FirstSelectedValue(value.Type));
        }
    }

    partial void OnBulkValueChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            BulkValue = clamped;
            return;
        }

        if (!_silentBulk && BulkType is { } option)
        {
            _setTypeOnSelected(option.Type, (byte)clamped);
        }
    }

    /// <summary>
    /// Pastes the copied values onto every selected light (the banks shown), each matched through its own profile: set
    /// many lights the same in one go.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPasteToSelected))]
    private void PasteToSelected()
    {
        foreach (var bank in SelectedBanks)
        {
            PasteOnto(bank);
        }
    }

    private bool CanPasteToSelected() => _clipboard.HasValues && SelectedBanks.Count > 0;

    /// <summary>Copies the first selected light (patch order): select one light on the left, then Copy.</summary>
    [RelayCommand(CanExecute = nameof(CanCopySelected))]
    private void CopySelected()
    {
        if (SelectedBanks.Count > 0)
        {
            CopyFrom(SelectedBanks[0]);
        }
    }

    private bool CanCopySelected() => SelectedBanks.Count > 0 && SelectedBanks[0].Channels.Count > 0;

    /// <summary>
    /// Copies a light's included channels (all channels when none is included yet). The whole mode is passed so a lone
    /// included second White is still pasted as the second White on another profile.
    /// </summary>
    private void CopyFrom(SceneFixtureTabViewModel bank)
    {
        var anyIncluded = bank.Channels.Any(f => f.IsIncluded);
        _clipboard.Copy(CopiedLightValues.CreateFromMode(
            bank.Fixture.ProfileId,
            bank.Header,
            bank.Channels.Select(f => (f.Channel, f.ByteValue, !anyIncluded || f.IsIncluded))));
    }

    /// <summary>Sets the matching channels of one light to the copied values and includes them in the step/scene.</summary>
    private void PasteOnto(SceneFixtureTabViewModel bank)
    {
        if (_clipboard.Current is not { } copied)
        {
            return;
        }

        var channels = bank.Channels.Select(f => f.Channel).ToList();
        foreach (var (index, value) in copied.MatchTo(bank.Fixture.ProfileId, channels))
        {
            var fader = bank.Channels[index];
            fader.IsIncluded = true;
            fader.Value = value;
        }
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasCopiedValues));
        OnPropertyChanged(nameof(CopiedText));
        PasteToSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        _batch = true;
        foreach (var bank in FilteredFixtures)
        {
            bank.IsSelected = true;
        }

        _batch = false;
        RebuildSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _batch = true;
        foreach (var bank in Fixtures)
        {
            bank.IsSelected = false;
        }

        _batch = false;
        RebuildSelection();
    }

    private void OnBankSelectionChanged(SceneFixtureTabViewModel bank)
    {
        if (!_batch)
        {
            RebuildSelection();
        }
    }

    private void ApplyFilter()
    {
        var text = SearchText?.Trim() ?? string.Empty;
        FilteredFixtures.Clear();
        foreach (var bank in Fixtures)
        {
            if (text.Length == 0 || bank.SearchKey.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFixtures.Add(bank);
            }
        }
    }

    /// <summary>
    /// Brings <see cref="SelectedBanks"/> in line with the ticks using minimal inserts/removes, so banks that stay
    /// selected keep their visuals.
    /// </summary>
    private void RebuildSelection()
    {
        var present = new HashSet<SceneFixtureTabViewModel>(Fixtures);
        for (var i = SelectedBanks.Count - 1; i >= 0; i--)
        {
            var bank = SelectedBanks[i];
            if (!bank.IsSelected || !present.Contains(bank))
            {
                SelectedBanks.RemoveAt(i);
            }
        }

        var next = 0;
        foreach (var bank in Fixtures)
        {
            if (!bank.IsSelected)
            {
                continue;
            }

            if (next < SelectedBanks.Count && ReferenceEquals(SelectedBanks[next], bank))
            {
                next++;
                continue;
            }

            var existing = SelectedBanks.IndexOf(bank);
            if (existing >= 0)
            {
                SelectedBanks.Move(existing, next);
            }
            else
            {
                SelectedBanks.Insert(next, bank);
            }

            next++;
        }

        SelectedCount = SelectedBanks.Count;
        PasteToSelectedCommand.NotifyCanExecuteChanged();
        CopySelectedCommand.NotifyCanExecuteChanged();
        RebuildBulkTypes();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    /// <summary>Lists the channel types of the selected lights, keeping the picked type when it is still there.</summary>
    private void RebuildBulkTypes()
    {
        var keep = BulkType?.Type;
        var channelCounts = new Dictionary<ChannelType, int>();
        var lightCounts = new Dictionary<ChannelType, int>();
        var seenInBank = new HashSet<ChannelType>();
        foreach (var bank in SelectedBanks)
        {
            seenInBank.Clear();
            foreach (var fader in bank.Channels)
            {
                var type = fader.Type;
                if (!SceneChannelTypeOption.IsBulkSettable(type))
                {
                    continue;
                }

                channelCounts[type] = channelCounts.GetValueOrDefault(type) + 1;
                if (seenInBank.Add(type))
                {
                    lightCounts[type] = lightCounts.GetValueOrDefault(type) + 1;
                }
            }
        }

        var types = channelCounts.Keys.ToList();
        types.Sort();
        _silentBulk = true;
        BulkTypes.Clear();
        SceneChannelTypeOption? next = null;
        foreach (var type in types)
        {
            var option = new SceneChannelTypeOption(type, channelCounts[type], lightCounts[type]);
            BulkTypes.Add(option);
            if (type == keep)
            {
                next = option;
            }
        }

        BulkType = next ?? (BulkTypes.Count > 0 ? BulkTypes[0] : null);
        _silentBulk = false;
        RefreshBulkValue();
        OnPropertyChanged(nameof(HasBulkTypes));
    }

    /// <summary>Value of the first selected light's first channel of the type (patch order), 0 when none.</summary>
    private int FirstSelectedValue(ChannelType type)
    {
        foreach (var bank in SelectedBanks)
        {
            foreach (var fader in bank.Channels)
            {
                if (fader.Type == type)
                {
                    return fader.ByteValue;
                }
            }
        }

        return 0;
    }

    private void SetBulkValueSilently(int value)
    {
        var wasSilent = _silentBulk;
        _silentBulk = true;
        BulkValue = value;
        _silentBulk = wasSilent;
    }
}
