using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One DMX channel in the channel layout editor: its function, an optional name (blank = the default name shown as
/// placeholder) and its default value. Changing the function also resets the default value until the user typed one.
/// </summary>
public sealed partial class ChannelLayoutRowViewModel : ObservableObject
{
    private readonly Action<ChannelLayoutRowViewModel> _onChanged;

    /// <summary>Documented ranges from a template channel; dropped when the function changes (safety rule 7).</summary>
    private List<ChannelCapability> _capabilities;

    /// <summary>True once the user typed a default value; then a function change keeps it.</summary>
    private bool _defaultEdited;
    private bool _settingDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName), nameof(ChipToolTip))]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeOption), nameof(ChipText), nameof(ColorHex), nameof(IsUnknown), nameof(AutomationName), nameof(ChipToolTip))]
    private ChannelType _type;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(AutomationName), nameof(ChipToolTip))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipToolTip))]
    private int _defaultValue;

    /// <summary>Name used while <see cref="Name"/> is blank ("Red 2", "Dimmer fine", "Channel 5"); set by the editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(AutomationName), nameof(ChipToolTip))]
    private string _placeholder = string.Empty;

    /// <summary>Mirrors the grid selection so the preview strip can highlight the chip.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public ChannelLayoutRowViewModel(ChannelLayoutRow row, Action<ChannelLayoutRowViewModel> onChanged)
    {
        _onChanged = onChanged;
        _type = row.Type;
        _name = row.Name;
        _defaultValue = row.DefaultValue;
        _capabilities = row.Capabilities.Select(c => c.Clone()).ToList();
        _defaultEdited = row.DefaultValue != ChannelLayout.DefaultValueFor(row.Type);
    }

    public ChannelLayoutTypeOption TypeOption => ChannelLayoutTypeOption.For(Type);

    /// <summary>Name that will be saved: the typed one, else the placeholder.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Placeholder : Name.Trim();

    public string ChipText => TypeOption.ChipText;

    /// <summary>Light colour of a colour channel ("#RRGGBB"); null for other functions.</summary>
    public string? ColorHex => TypeOption.ColorHex;

    public bool IsUnknown => Type == ChannelType.Generic;

    /// <summary>Losing this row loses work: it has a function or a typed name.</summary>
    public bool IsMeaningful => Type != ChannelType.Generic || !string.IsNullOrWhiteSpace(Name);

    public string AutomationName => $"Channel {Number}: {TypeOption.Label}, {DisplayName}";

    public string ChipToolTip => $"Channel {Number} · {DisplayName} ({TypeOption.Label}) · default {DefaultValue}";

    /// <summary>Snapshot for <see cref="ChannelLayout"/>; a blank name lets it pick the default name.</summary>
    public ChannelLayoutRow ToRow() => new()
    {
        Type = Type,
        Name = Name.Trim(),
        DefaultValue = (byte)Math.Clamp(DefaultValue, 0, 255),
        Capabilities = _capabilities.Select(c => c.Clone()).ToList(),
    };

    /// <summary>Copy for "Duplicate"; keeps a typed default value.</summary>
    public ChannelLayoutRowViewModel Clone() => new(ToRow(), _onChanged) { _defaultEdited = _defaultEdited };

    partial void OnTypeChanged(ChannelType value)
    {
        _capabilities = [];
        if (!_defaultEdited)
        {
            _settingDefault = true;
            try
            {
                DefaultValue = ChannelLayout.DefaultValueFor(value);
            }
            finally
            {
                _settingDefault = false;
            }
        }

        _onChanged(this);
    }

    partial void OnNameChanged(string value) => _onChanged(this);

    partial void OnDefaultValueChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            DefaultValue = clamped;
            return;
        }

        if (!_settingDefault)
        {
            _defaultEdited = true;
        }

        _onChanged(this);
    }
}
