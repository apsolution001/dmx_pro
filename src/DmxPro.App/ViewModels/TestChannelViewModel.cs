using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Profiles;
using DmxPro.Engine.Testing;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One channel fader of a light on the Test &amp; Preview page. While not under test it follows the live output
/// (polled, updated silently); moving it makes this channel a test override ("take over on touch") until released.
/// The meaning comes from the profile; unknown channels and unverified profiles are flagged (safety rule 7).
/// </summary>
public sealed class TestChannelViewModel : ObservableObject, IToolTipSource
{
    // Cached event args for the properties the ~30 Hz poll raises, so live updates allocate no event args.
    private static readonly PropertyChangedEventArgs ValueChangedArgs = new(nameof(Value));
    private static readonly PropertyChangedEventArgs ByteValueChangedArgs = new(nameof(ByteValue));
    private static readonly PropertyChangedEventArgs CapabilityLabelChangedArgs = new(nameof(CapabilityLabel));
    private static readonly PropertyChangedEventArgs OutputValueChangedArgs = new(nameof(OutputValue));

    private readonly Func<TestChannelViewModel, byte, bool> _onUserSet;
    private readonly bool _profileUnverified;
    private byte _value;
    private byte _outputValue;
    private bool _isOverridden;
    private bool _isStrobeBlocked;
    private string? _outputNote;

    // What the current _outputNote was formatted from; the note is only re-formatted when one of these changes.
    private bool _noteOverridden;
    private byte _noteValue;
    private byte _noteOutput;
    private string? _noteReason;

    internal TestChannelViewModel(
        TestFixtureRowViewModel row,
        ChannelDefinition channel,
        int number,
        int universe,
        int dmxAddress,
        bool profileUnverified,
        bool isAvailable,
        bool isUndefined,
        Func<TestChannelViewModel, byte, bool> onUserSet,
        Action<TestChannelViewModel> onRelease)
    {
        Row = row;
        Channel = channel;
        Number = number;
        Universe = universe;
        DmxAddress = dmxAddress;
        _profileUnverified = profileUnverified;
        IsAvailable = isAvailable;
        IsUndefined = isUndefined;
        _onUserSet = onUserSet;
        _value = channel.Default;
        _outputValue = channel.Default;
        ReleaseCommand = new RelayCommand(() => onRelease(this), () => _isOverridden);
    }

    /// <summary>The light this channel belongs to.</summary>
    internal TestFixtureRowViewModel Row { get; }

    public Guid FixtureId => Row.FixtureId;

    /// <summary>The profile channel (a placeholder when the mode names a key the profile does not define).</summary>
    public ChannelDefinition Channel { get; }

    public string Key => Channel.Key;

    public ChannelType Type => Channel.Type;

    /// <summary>Channel number within the fixture, 1-based.</summary>
    public int Number { get; }

    /// <summary>Project universe, from 1.</summary>
    public int Universe { get; }

    /// <summary>Absolute DMX address (may be above 512 for a footprint past the universe end; then not available).</summary>
    public int DmxAddress { get; }

    /// <summary>The mode lists a channel key the profile does not define.</summary>
    public bool IsUndefined { get; }

    public string Name => string.IsNullOrWhiteSpace(Channel.Name) ? Channel.Key : Channel.Name;

    public string TypeName => Channel.Type == ChannelType.Generic ? "Unknown" : Channel.Type.ToString();

    public bool IsUnknown => Channel.Type == ChannelType.Generic;

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

    /// <summary>
    /// Tint of the channel icon on the desk: the light colour, except White (it would vanish on a light theme), else null.
    /// </summary>
    public string? IconColorHex => Channel.Type == ChannelType.White ? null : ColorHex;

    public bool HasIconColor => IconColorHex is not null;

    /// <summary>
    /// False when the channel is not patched in the engine (disabled light, past 512, undefined key): the fader is
    /// disabled.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Fader / value box. Shows the live output while not under test; setting it (user) puts this channel under test
    /// with the rounded, clamped value. Refused (snaps back) when the page is not active or the row is stale.
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            var level = ChannelTestSession.ToByte(value);
            if (!IsAvailable || !_onUserSet(this, level))
            {
                // Refused: snap the slider/box back to what is shown.
                OnPropertyChanged();
                return;
            }

            var changed = level != _value;
            SetIsOverridden(true);
            if (changed)
            {
                _value = level;
                OnValueChanged();
            }
            else if (level != value)
            {
                // The box still shows the out-of-range/fractional input; refresh it.
                OnPropertyChanged();
            }

            // The output note is left to the next poll (it knows the reason); clearing it here would flicker.
        }
    }

    /// <summary>The value as a byte.</summary>
    public byte ByteValue => _value;

    /// <summary>What actually goes out (latest snapshot).</summary>
    public byte OutputValue => _outputValue;

    /// <summary>
    /// Under test, but what goes out differs (Grand Master, Blackout, strobe safety, another light sharing the address).
    /// </summary>
    public bool OutputDiffers => _outputNote is not null;

    /// <summary>"out 128 (Grand Master)" while <see cref="OutputDiffers"/>, else null.</summary>
    public string? OutputNote => _outputNote;

    public bool IsOverridden => _isOverridden;

    public IRelayCommand ReleaseCommand { get; }

    /// <summary>A Strobe channel held at its safe value because strobe is not allowed in Settings.</summary>
    public bool IsStrobeBlocked => _isStrobeBlocked;

    /// <summary>
    /// What the profile says the value does; "⚠" marks an unverified profile. For a strobe channel under test while
    /// strobe is blocked it describes what is really sent ("blocked — sends 0 (No strobe)"), never the requested value.
    /// </summary>
    public string? CapabilityLabel
    {
        get
        {
            var prefix = _profileUnverified ? "⚠ " : string.Empty;
            if (_isStrobeBlocked && _isOverridden)
            {
                var sent = Channel.FindCapability(_outputValue)?.Label;
                return string.IsNullOrWhiteSpace(sent)
                    ? $"{prefix}blocked — sends {_outputValue}"
                    : $"{prefix}blocked — sends {_outputValue} ({sent})";
            }

            var label = Channel.FindCapability(_value)?.Label;
            return string.IsNullOrWhiteSpace(label) ? null : prefix + label;
        }
    }

    /// <summary>
    /// Built on demand when the tooltip opens (<c>Controls/LazyToolTip</c>), so no change notification is raised for it:
    /// the value changes up to ~30 times a second for every fader.
    /// </summary>
    public string ToolTip
    {
        get
        {
            var text = $"{Name} ({TypeName}) · channel {Number} · U{Universe} DMX {DmxAddress} · value {_value} · default {Channel.Default}";
            if (CapabilityLabel is { } capability)
            {
                text += $" · {capability}";
            }

            if (IsUndefined)
            {
                text += " · not defined in the profile";
            }
            else if (IsUnknown)
            {
                text += " · meaning unknown (not confirmed by the profile)";
            }

            if (_profileUnverified)
            {
                text += " · profile not verified on a real fixture";
            }

            if (!IsAvailable)
            {
                return text + Environment.NewLine + "This channel is not sent (see the light's problem note).";
            }

            text += _isOverridden ? " · under test" : " · showing live output";
            if (_outputNote is { } note)
            {
                text += $" · {note}";
            }

            if (_isStrobeBlocked)
            {
                text += " · strobe is not allowed in Settings, so the safe value is sent";
            }
            else if (Channel.Type == ChannelType.Strobe)
            {
                text += " · the max strobe rate setting does not limit a light's own strobe channel";
            }

            return text + Environment.NewLine + "Move the fader or type 0–255 to test · Release returns it to what runs";
        }
    }

    public string AutomationName => $"{Name}, channel {Number}, DMX {DmxAddress}, 0 to 255";

    /// <summary>
    /// Poll update: the live output value. While not under test the fader follows it silently. <paramref name="reason"/>
    /// explains a difference between the test value and the output (null when not known: assumed pending).
    /// </summary>
    internal void UpdateLive(byte output, bool strobeBlocked, string? reason)
    {
        var labelChanged = false;
        if (_outputValue != output)
        {
            _outputValue = output;
            OnPropertyChanged(OutputValueChangedArgs);
            labelChanged = _isStrobeBlocked && _isOverridden;
        }

        if (_isStrobeBlocked != strobeBlocked)
        {
            _isStrobeBlocked = strobeBlocked;
            OnPropertyChanged(nameof(IsStrobeBlocked));
            labelChanged = true;
        }

        if (!_isOverridden && _value != output)
        {
            _value = output;
            OnValueChanged();
        }
        else if (labelChanged)
        {
            OnPropertyChanged(nameof(CapabilityLabel));
        }

        RefreshOutputNote(reason);
    }

    /// <summary>Puts the channel under test silently (rebuild with a value kept by the session).</summary>
    internal void RestoreOverride(byte value)
    {
        SetIsOverridden(true);
        if (_value != value)
        {
            _value = value;
            OnValueChanged();
        }
    }

    /// <summary>No longer under test: the next poll shows the live value again.</summary>
    internal void MarkReleased()
    {
        SetIsOverridden(false);
        RefreshOutputNote(null);
    }

    private void RefreshOutputNote(string? reason)
    {
        if (_noteOverridden == _isOverridden && _noteValue == _value && _noteOutput == _outputValue
            && string.Equals(_noteReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _noteOverridden = _isOverridden;
        _noteValue = _value;
        _noteOutput = _outputValue;
        _noteReason = reason;
        var note = _isOverridden && reason is not null && _outputValue != _value ? $"out {_outputValue} ({reason})" : null;
        if (note == _outputNote)
        {
            return;
        }

        _outputNote = note;
        OnPropertyChanged(nameof(OutputNote));
        OnPropertyChanged(nameof(OutputDiffers));
    }

    private void SetIsOverridden(bool value)
    {
        if (_isOverridden == value)
        {
            return;
        }

        _isOverridden = value;
        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(CapabilityLabel));
        ReleaseCommand.NotifyCanExecuteChanged();
    }

    private void OnValueChanged()
    {
        OnPropertyChanged(ValueChangedArgs);
        OnPropertyChanged(ByteValueChangedArgs);
        OnPropertyChanged(CapabilityLabelChangedArgs);
    }
}
