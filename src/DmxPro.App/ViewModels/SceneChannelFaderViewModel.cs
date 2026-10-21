using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One vertical channel fader of a fixture in the scene editor. The channel meaning comes from the profile; an
/// unchecked <see cref="IsIncluded"/> means the scene does not control the channel at all. Unknown
/// (<see cref="ChannelType.Generic"/>) channels are shown as raw channels, never with a confirmed meaning.
/// </summary>
public sealed partial class SceneChannelFaderViewModel : ObservableObject
{
    private readonly Action<SceneChannelFaderViewModel> _onChanged;
    private byte _value;
    private bool _isIncluded;
    private bool _silent;

    public SceneChannelFaderViewModel(
        Guid fixtureId,
        ChannelDefinition channel,
        int number,
        int dmxAddress,
        byte value,
        bool isIncluded,
        Action<SceneChannelFaderViewModel> onChanged)
    {
        FixtureId = fixtureId;
        Channel = channel;
        Number = number;
        DmxAddress = dmxAddress;
        _value = value;
        _isIncluded = isIncluded;
        _onChanged = onChanged;
    }

    public Guid FixtureId { get; }

    /// <summary>The profile channel this fader controls.</summary>
    public ChannelDefinition Channel { get; }

    public ChannelType Type => Channel.Type;

    /// <summary>Channel number within the fixture, 1-based.</summary>
    public int Number { get; }

    /// <summary>Absolute DMX address (1–512) in the fixture's universe.</summary>
    public int DmxAddress { get; }

    public string Name => string.IsNullOrWhiteSpace(Channel.Name) ? Channel.Key : Channel.Name;

    public string TypeName => Channel.Type == ChannelType.Generic ? "Unknown" : Channel.Type.ToString();

    public bool IsUnknown => Channel.Type == ChannelType.Generic;

    /// <summary>"DMX 17": the absolute address, shown under the fader.</summary>
    public string AddressText => $"DMX {DmxAddress}";

    public string IconKind => Channel.Type switch
    {
        ChannelType.Dimmer => "Dimmer",
        _ when Channel.Type.IsColor() => "Color",
        ChannelType.Strobe => "Strobe",
        _ => "Channel",
    };

    /// <summary>Light colour of a colour channel ("#RRGGBB"), null for other channels.</summary>
    public string? ColorHex => Channel.Type switch
    {
        ChannelType.Red => "#FF0000",
        ChannelType.Green => "#00FF00",
        ChannelType.Blue => "#0000FF",
        ChannelType.White => "#FFFFFF",
        ChannelType.Amber => "#FFBF00",
        ChannelType.UV => "#8A2BE2",
        _ => null,
    };

    /// <summary>Whether the scene controls this channel. Unchecking removes its value from the scene.</summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (SetProperty(ref _isIncluded, value) && !_silent)
            {
                _onChanged(this);
            }
        }
    }

    /// <summary>Level 0–255 (rounded and clamped). Moving the fader of an excluded channel includes it.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var level = SceneEditing.ToDmxValue(value);
            if (level == _value)
            {
                if (level != value)
                {
                    // The text box still shows the out-of-range/fractional input; refresh it.
                    OnPropertyChanged();
                }

                return;
            }

            _value = level;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CapabilityLabel));
            OnPropertyChanged(nameof(ToolTip));
            if (_silent)
            {
                return;
            }

            if (!_isIncluded)
            {
                _isIncluded = true;
                OnPropertyChanged(nameof(IsIncluded));
            }

            _onChanged(this);
        }
    }

    public byte ByteValue => _value;

    /// <summary>What the profile says the current value does ("No strobe", …).</summary>
    public string? CapabilityLabel
    {
        get
        {
            var label = Channel.FindCapability(_value)?.Label;
            if (string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            return label;
        }
    }

    public string ToolTip
    {
        get
        {
            var text = $"{Name} ({TypeName}) · channel {Number} · DMX {DmxAddress} · value {_value} · default {Channel.Default}";
            if (CapabilityLabel is { } capability)
            {
                text += $" · {capability}";
            }

            if (IsUnknown)
            {
                text += " · meaning unknown: raw channel";
            }

            return text + Environment.NewLine + "Type 0–255 in the box · ↑/↓ ±1 · PageUp/PageDown ±10 · tick = included in the scene";
        }
    }

    public string AutomationName => $"{Name}, channel {Number}, 0 to 255";

    /// <summary>Updates the shown value from the editor without calling back (e.g. a colour row moved).</summary>
    public void SetValueSilently(byte value)
    {
        _silent = true;
        Value = value;
        _silent = false;
    }

    /// <summary>Updates the tick from the editor without calling back.</summary>
    public void SetIncludedSilently(bool value)
    {
        _silent = true;
        IsIncluded = value;
        _silent = false;
    }
}
