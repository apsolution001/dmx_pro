using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One light in the sequence editor's Colours view: name, its colour in the selected step and whether it is selected.
/// The selection is the same as in the All channels view (it lives on the light's fader bank), so switching views
/// keeps the chosen lights.
/// </summary>
public sealed class SequenceLightRowViewModel : ObservableObject
{
    private string _swatchHex;
    private string _swatchToolTip;

    public SequenceLightRowViewModel(SceneFixtureTabViewModel bank, SequenceSwatch swatch)
    {
        Bank = bank;
        _swatchHex = swatch.Hex;
        _swatchToolTip = swatch.ToolTip;
        bank.PropertyChanged += OnBankChanged;
    }

    public SceneFixtureTabViewModel Bank { get; }

    public Guid FixtureId => Bank.FixtureId;

    public string Name => Bank.Header;

    /// <summary>"U1 · 1–8 · 8ch".</summary>
    public string Detail => Bank.Detail;

    public bool IsSelected
    {
        get => Bank.IsSelected;
        set => Bank.IsSelected = value;
    }

    /// <summary>On-screen colour of the light in this step ("#RRGGBB", display only).</summary>
    public string SwatchHex => _swatchHex;

    public string SwatchToolTip => _swatchToolTip;

    public string AutomationName => $"{Name}, {Detail}";

    public void SetSwatch(SequenceSwatch swatch)
    {
        if (swatch.Hex == _swatchHex && swatch.ToolTip == _swatchToolTip)
        {
            return;
        }

        _swatchHex = swatch.Hex;
        _swatchToolTip = swatch.ToolTip;
        OnPropertyChanged(nameof(SwatchHex));
        OnPropertyChanged(nameof(SwatchToolTip));
    }

    private void OnBankChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneFixtureTabViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}
