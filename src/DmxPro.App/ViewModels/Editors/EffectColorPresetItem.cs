using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One colour preset button of <see cref="ColorEditorViewModel"/> (the scene editor's presets), marked when the effect's
/// colour is exactly this preset.
/// </summary>
public sealed class EffectColorPresetItem : ObservableObject
{
    private bool _isSelected;

    public EffectColorPresetItem(SceneEditorColorPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Preset = preset;
        Color = RgbColor.TryParseHex(preset.Swatch, out var color) ? color : new RgbColor(255, 255, 255);
    }

    public SceneEditorColorPreset Preset { get; }

    /// <summary>The colour this preset gives an effect (red, green and blue only; white/amber/UV follow the colour policy).</summary>
    public RgbColor Color { get; }

    public string Name => Preset.Name;

    /// <summary>"#RRGGBB" (a real light colour) for the swatch.</summary>
    public string Swatch => Preset.Swatch;

    public string ToolTip => Preset.ToolTip;

    /// <summary>The effect's colour is this preset.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    /// <summary>"Colour Red" / "Colour Red, chosen".</summary>
    public string AutomationName => IsSelected ? $"Colour {Name}, chosen" : $"Colour {Name}";
}
