using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Patching;

namespace DmxPro.App.ViewModels;

/// <summary>
/// The fader bank of one fixture in the scene: a vertical fader per channel of its profile mode. Shown in the
/// Channels tab while <see cref="IsSelected"/>.
/// </summary>
public sealed partial class SceneFixtureTabViewModel : SceneEditorTab
{
    private readonly Action<SceneFixtureTabViewModel, bool> _onSetAllIncluded;
    private Action<SceneFixtureTabViewModel>? _onSelectionChanged;
    private bool _silent;

    [ObservableProperty]
    private bool _isSelected;

    public SceneFixtureTabViewModel(
        Fixture fixture,
        string detail,
        bool profileUnverified,
        Action<SceneFixtureTabViewModel, bool> onSetAllIncluded)
    {
        Fixture = fixture;
        Detail = detail;
        ProfileUnverified = profileUnverified;
        _onSetAllIncluded = onSetAllIncluded;
        SearchKey = $"{fixture.Name} {detail}";
    }

    public Fixture Fixture { get; }

    public Guid FixtureId => Fixture.Id;

    public override string Header => Fixture.Name;

    public override string IconKind => "Fixtures";

    /// <summary>"U1 · 1–8 · 8ch Full".</summary>
    public string Detail { get; }

    public bool ProfileUnverified { get; }

    public ObservableCollection<SceneChannelFaderViewModel> Channels { get; } = [];

    public bool HasNoChannels => Channels.Count == 0;

    /// <summary>Text the Channels tab's search box matches against (name + detail).</summary>
    internal string SearchKey { get; }

    /// <summary>Wires the bank to the Channels tab's selection; set once by the tab.</summary>
    internal void AttachSelection(Action<SceneFixtureTabViewModel> onSelectionChanged) => _onSelectionChanged = onSelectionChanged;

    internal void SetSelectedSilently(bool value)
    {
        _silent = true;
        IsSelected = value;
        _silent = false;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_silent)
        {
            _onSelectionChanged?.Invoke(this);
        }
    }

    [RelayCommand]
    private void EnableAll() => _onSetAllIncluded(this, true);

    [RelayCommand]
    private void DisableAll() => _onSetAllIncluded(this, false);
}
