using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One address of the Test page DMX monitor. The 512 cells are created once and updated in place; PropertyChanged is
/// raised only when something really changed.
/// </summary>
public sealed class DmxMonitorCellViewModel : ObservableObject, IToolTipSource
{
    private int _universe = 1;
    private byte _value;
    private bool _isPatched;
    private bool _isInSelectedFixture;
    private bool _isOverridden;
    private string? _channelLabel;

    public DmxMonitorCellViewModel(int address)
    {
        Address = address;
        AddressText = address.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>DMX address 1–512.</summary>
    public int Address { get; }

    /// <summary>"001".</summary>
    public string AddressText { get; }

    /// <summary>Value actually sent (latest engine snapshot).</summary>
    public byte Value => _value;

    public string ValueText => _value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An enabled fixture channel sits at this address.</summary>
    public bool IsPatched => _isPatched;

    /// <summary>The address belongs to the fixture selected in the list.</summary>
    public bool IsInSelectedFixture => _isInSelectedFixture;

    /// <summary>A Test page value is applied at this address.</summary>
    public bool IsOverridden => _isOverridden;

    /// <summary>
    /// "U1 · 017 · Stage L PAR · Red · 200 (test)". Built when the tooltip opens (<c>Controls/LazyToolTip</c>), so no
    /// change notification is raised for it.
    /// </summary>
    public string ToolTip =>
        $"U{_universe} · {AddressText} · {_channelLabel ?? "not patched"} · {_value}{(_isOverridden ? " (test)" : string.Empty)}";

    internal void SetValue(byte value)
    {
        if (_value == value)
        {
            return;
        }

        _value = value;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
    }

    internal void SetOverridden(bool value)
    {
        if (_isOverridden == value)
        {
            return;
        }

        _isOverridden = value;
        OnPropertyChanged(nameof(IsOverridden));
    }

    internal void SetSelected(bool value)
    {
        if (_isInSelectedFixture != value)
        {
            _isInSelectedFixture = value;
            OnPropertyChanged(nameof(IsInSelectedFixture));
        }
    }

    /// <summary>Patch info for the shown universe: which light/channel sits here (null = nothing).</summary>
    internal void SetPatch(int universe, string? channelLabel)
    {
        var patched = channelLabel is not null;
        _universe = universe;
        _channelLabel = channelLabel;
        if (_isPatched != patched)
        {
            _isPatched = patched;
            OnPropertyChanged(nameof(IsPatched));
        }
    }
}
