using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One light in the chaser step-look panel's Colours view: name, its colour in the step and whether it is selected.
/// The selection lives on the light's fader bank (shared with the All channels view), so switching views keeps the
/// chosen lights.
/// </summary>
public sealed class ChaserStepLookLightRowViewModel : ObservableObject
{
    private string _swatchHex;
    private string _swatchToolTip;

    public ChaserStepLookLightRowViewModel(SceneFixtureTabViewModel bank, string swatchHex, string swatchToolTip)
    {
        ArgumentNullException.ThrowIfNull(bank);
        Bank = bank;
        _swatchHex = swatchHex;
        _swatchToolTip = swatchToolTip;
        bank.PropertyChanged += OnBankChanged;
    }

    public SceneFixtureTabViewModel Bank { get; }

    public Guid FixtureId => Bank.FixtureId;

    public string Name => Bank.Header;

    /// <summary>"U1 · 1–8 · 6ch".</summary>
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

    public void SetSwatch(string hex, string toolTip)
    {
        if (hex == _swatchHex && toolTip == _swatchToolTip)
        {
            return;
        }

        _swatchHex = hex;
        _swatchToolTip = toolTip;
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
