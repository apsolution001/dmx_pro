using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// An effect colour: the scene editor's colour presets (the chosen one is marked) plus a "Custom" part with Red / Green /
/// Blue levels 0–255 and a "#RRGGBB" box. Every change (not the silent ones from the editor) is reported through the
/// callback. The custom part can be folded away (<see cref="IsCustomOpen"/>); it opens by itself for a colour that is
/// not a preset.
/// </summary>
public sealed partial class ColorEditorViewModel : ObservableObject
{
    private readonly Action<RgbColor> _onChanged;
    private RgbColor _color = new(255, 255, 255);
    private bool _silent;

    /// <summary>The Red / Green / Blue sliders and the hex box are shown. Default on (the colour cycle editor shows them).</summary>
    [ObservableProperty]
    private bool _isCustomOpen = true;

    public ColorEditorViewModel(Action<RgbColor> onChanged)
    {
        _onChanged = onChanged;
        PresetItems = SceneEditorColorPreset.All.Select(p => new EffectColorPresetItem(p)).ToList();
        SyncPresets();
    }

    public RgbColor Color => _color;

    public IReadOnlyList<SceneEditorColorPreset> Presets => SceneEditorColorPreset.All;

    /// <summary>The presets as buttons that know whether they are the current colour.</summary>
    public IReadOnlyList<EffectColorPresetItem> PresetItems { get; }

    /// <summary>The colour is not one of the presets.</summary>
    public bool IsCustomColor => PresetItems.All(p => !p.IsSelected);

    /// <summary>"Red", or "Custom colour" when the colour is not a preset.</summary>
    public string ColorName => PresetItems.FirstOrDefault(p => p.IsSelected)?.Name ?? "Custom colour";

    /// <summary>"red", or "this colour" — for sentences such as "the lights go full red".</summary>
    public string ColorNameInSentence => PresetItems.FirstOrDefault(p => p.IsSelected)?.Name.ToLowerInvariant() ?? "this colour";

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
        if (IsCustomColor)
        {
            IsCustomOpen = true;
        }
    }

    [RelayCommand]
    private void ApplyPreset(SceneEditorColorPreset? preset)
    {
        if (preset is not null && RgbColor.TryParseHex(preset.Swatch, out var color))
        {
            Apply(color, notify: true);
        }
    }

    [RelayCommand]
    private void ApplyPresetItem(EffectColorPresetItem? item)
    {
        if (item is not null)
        {
            Apply(item.Color, notify: true);
        }
    }

    private void SyncPresets()
    {
        var found = false;
        foreach (var item in PresetItems)
        {
            var match = !found && item.Color == _color;
            found |= match;
            item.IsSelected = match;
        }

        OnPropertyChanged(nameof(IsCustomColor));
        OnPropertyChanged(nameof(ColorName));
        OnPropertyChanged(nameof(ColorNameInSentence));
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
        SyncPresets();
        if (notify && !_silent)
        {
            _onChanged(color);
        }
    }
}
