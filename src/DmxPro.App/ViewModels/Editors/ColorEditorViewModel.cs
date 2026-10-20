using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// An effect colour: the scene editor's colour presets plus Red / Green / Blue levels 0–255 and a "#RRGGBB" box.
/// Every change (not the silent ones from the editor) is reported through the callback.
/// </summary>
public sealed partial class ColorEditorViewModel : ObservableObject
{
    private readonly Action<RgbColor> _onChanged;
    private RgbColor _color = new(255, 255, 255);
    private bool _silent;

    public ColorEditorViewModel(Action<RgbColor> onChanged)
    {
        _onChanged = onChanged;
    }

    public RgbColor Color => _color;

    public IReadOnlyList<SceneEditorColorPreset> Presets => SceneEditorColorPreset.All;

    /// <summary>"#RRGGBB" of the colour (a real light colour) for the swatch.</summary>
    public string SwatchHex => _color.ToHex();

    public int Red
    {
        get => _color.R;
        set => SetComponent(value, _color with { R = Clamp(value) }, nameof(Red));
    }

    public int Green
    {
        get => _color.G;
        set => SetComponent(value, _color with { G = Clamp(value) }, nameof(Green));
    }

    public int Blue
    {
        get => _color.B;
        set => SetComponent(value, _color with { B = Clamp(value) }, nameof(Blue));
    }

    /// <summary>"#RRGGBB"; text that is not a colour reverts.</summary>
    public string Hex
    {
        get => _color.ToHex();
        set
        {
            if (RgbColor.TryParseHex(value, out var color))
            {
                Apply(color, notify: true);
            }

            // Show the normalised (or reverted) text.
            OnPropertyChanged();
        }
    }

    /// <summary>Shows a colour without reporting a change.</summary>
    public void SetColorSilently(RgbColor color)
    {
        _silent = true;
        Apply(color, notify: false);
        _silent = false;
    }

    [RelayCommand]
    private void ApplyPreset(SceneEditorColorPreset? preset)
    {
        if (preset is not null && RgbColor.TryParseHex(preset.Swatch, out var color))
        {
            Apply(color, notify: true);
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private void SetComponent(int requested, RgbColor next, string propertyName)
    {
        if (next == _color)
        {
            if (requested is < 0 or > 255)
            {
                // The box still shows the out-of-range input; refresh it.
                OnPropertyChanged(propertyName);
            }

            return;
        }

        Apply(next, notify: true);
    }

    private void Apply(RgbColor color, bool notify)
    {
        if (color == _color)
        {
            return;
        }

        _color = color;
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(SwatchHex));
        OnPropertyChanged(nameof(Color));
        if (notify && !_silent)
        {
            _onChanged(color);
        }
    }
}
