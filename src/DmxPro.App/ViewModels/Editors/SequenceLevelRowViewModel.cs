using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One slider of the sequence editor's Colours view: Intensity (Dimmer) or a colour (Red, Green, …) for every selected
/// light of the step at once. The channel is found through each light's profile by <see cref="ChannelType"/>; the row
/// only exists when a selected light has that channel type.
/// </summary>
public sealed class SequenceLevelRowViewModel : ObservableObject
{
    private readonly Action<ChannelType, byte> _set;
    private int _value;

    public SequenceLevelRowViewModel(ChannelType type, int value, Action<ChannelType, byte> set)
    {
        Type = type;
        _value = Math.Clamp(value, 0, 255);
        _set = set;
    }

    public ChannelType Type { get; }

    /// <summary>"Intensity", "Red", "UV", …</summary>
    public string Label => Type switch
    {
        ChannelType.Dimmer => "Intensity",
        ChannelType.UV => "UV",
        _ => Type.ToString(),
    };

    /// <summary>Real light colour of the marker dot ("#RRGGBB"); null for Intensity (shown as an icon).</summary>
    public string? DotHex => Type switch
    {
        ChannelType.Red => "#FF0000",
        ChannelType.Green => "#00FF00",
        ChannelType.Blue => "#0000FF",
        ChannelType.White => "#FFFFFF",
        ChannelType.Amber => "#FFBF00",
        ChannelType.UV => "#8A2BE2",
        _ => null,
    };

    public bool IsIntensity => Type == ChannelType.Dimmer;

    public string AutomationName => $"{Label} of the selected lights, 0 to 255";

    public string ToolTip => IsIntensity
        ? "Intensity (dimmer) of every selected light in this step · 0 = off, 255 = full"
        : $"{Label} level of every selected light in this step · 0–255";

    /// <summary>Level 0–255; setting it writes the level to every selected light of the step.</summary>
    public int Value
    {
        get => _value;
        set
        {
            var level = Math.Clamp(value, 0, 255);
            if (level == _value)
            {
                if (level != value)
                {
                    OnPropertyChanged();
                }

                return;
            }

            _value = level;
            OnPropertyChanged();
            _set(Type, (byte)level);
        }
    }

    /// <summary>Shows a level without writing it (the lights changed elsewhere).</summary>
    public void SetValueSilently(int value)
    {
        var level = Math.Clamp(value, 0, 255);
        if (level != _value)
        {
            _value = level;
            OnPropertyChanged(nameof(Value));
        }
    }
}
