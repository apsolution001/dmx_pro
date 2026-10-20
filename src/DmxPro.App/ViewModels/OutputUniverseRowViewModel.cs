using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Output;
using DmxPro.Output.Configuration;
using DmxPro.Output.Protocols;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One project universe on the Input / Output page and the output it is sent through. Holds the settings of every
/// output type so switching the type back and forth does not lose what the user typed.
/// </summary>
/// <remarks>
/// Art-Net / sACN universes are shown in protocol numbering here only (Art-Net Port-Address from 0, sACN universe
/// from 1); everywhere else the app uses project universes from 1.
/// </remarks>
public sealed partial class OutputUniverseRowViewModel : ObservableObject
{
    private static readonly HashSet<string> ConfigProperties =
    [
        nameof(OutputType), nameof(ArtNetDestination), nameof(ArtNetPortAddress), nameof(BindAddress),
        nameof(SacnUniverse), nameof(SacnPriority), nameof(SacnMulticast), nameof(SacnUnicastAddress),
        nameof(SacnSourceName), nameof(SerialPort), nameof(FtdiSerialNumber), nameof(FrameRateHz),
    ];

    private int _artNetUdpPort = ArtNetPacket.DefaultPort;
    private int _sacnUdpPort = SacnPacket.DefaultPort;

    [ObservableProperty]
    private bool _isInProject;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private UniverseOutputType _outputType;

    [ObservableProperty]
    private string _artNetDestination = ArtNetOutputConfig.LimitedBroadcast;

    /// <summary>Art-Net Port-Address (0–32767, protocol numbering).</summary>
    [ObservableProperty]
    private double? _artNetPortAddress;

    private string _bindAddress = string.Empty;

    /// <summary>sACN universe (1–63999, protocol numbering).</summary>
    [ObservableProperty]
    private double? _sacnUniverse;

    [ObservableProperty]
    private double? _sacnPriority = SacnPacket.DefaultPriority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SacnUnicast))]
    private bool _sacnMulticast = true;

    [ObservableProperty]
    private string _sacnUnicastAddress = string.Empty;

    [ObservableProperty]
    private string _sacnSourceName = SacnOutputConfig.DefaultSourceName;

    [ObservableProperty]
    private string _serialPort = string.Empty;

    private string _ftdiSerialNumber = string.Empty;
    private bool _suppressConfigurationChanged;

    [ObservableProperty]
    private double? _frameRateHz = OutputConfig.DefaultFrameRateHz;

    /// <summary>Runtime state of this universe's output (never persisted).</summary>
    [ObservableProperty]
    private OutputState _state = OutputState.Disabled;

    [ObservableProperty]
    private string _statusText = "—";

    /// <summary>Problems found when output was last enabled / validated (null = none).</summary>
    [ObservableProperty]
    private string? _issueText;

    [ObservableProperty]
    private bool _issueIsError;

    public OutputUniverseRowViewModel(int universe, bool isInProject)
    {
        Universe = universe;
        _isInProject = isInProject;
        _artNetPortAddress = universe - 1;
        _sacnUniverse = universe;
    }

    /// <summary>Raised when a setting that goes into the output configuration changed.</summary>
    public event EventHandler? ConfigurationChanged;

    /// <summary>Configuration id (stable while the app runs and across saves).</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Project universe (UI numbering, from 1).</summary>
    public int Universe { get; }

    public string UniverseLabel => $"Universe {Universe}";

    public string UsageText => IsInProject ? "Used by fixtures" : "No fixtures yet";

    public bool IsConfigured => OutputType != UniverseOutputType.None;

    /// <summary>Adapter IPv4 to send from; empty = Windows chooses. Shared by Art-Net and sACN.</summary>
    /// <remarks>Ignores null: a picker pushes null while its item list is refreshed, which must not erase the setting.</remarks>
    public string BindAddress
    {
        get => _bindAddress;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _bindAddress, value);
            }
        }
    }

    /// <summary>FTDI serial number for Open DMX; empty = first device. Ignores null like <see cref="BindAddress"/>.</summary>
    public string FtdiSerialNumber
    {
        get => _ftdiSerialNumber;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _ftdiSerialNumber, value);
            }
        }
    }

    public bool SacnUnicast
    {
        get => !SacnMulticast;
        set => SacnMulticast = !value;
    }

    public string OutputTypeName => OutputTypeOption.All.First(o => o.Type == OutputType).Name;

    public string TypeDescription => OutputTypeOption.All.First(o => o.Type == OutputType).Description;

    /// <summary>One-line summary for the table, e.g. "Art-Net → 192.168.1.50 · Art-Net universe 0".</summary>
    public string Summary => OutputType switch
    {
        UniverseOutputType.Virtual => "Loopback, nothing leaves this PC",
        UniverseOutputType.ArtNet => $"→ {Display(ArtNetDestination, "no address")} · Art-Net universe {PortAddressValue}",
        UniverseOutputType.Sacn => SacnMulticast
            ? $"sACN universe {SacnUniverseValue} · multicast"
            : $"sACN universe {SacnUniverseValue} → {Display(SacnUnicastAddress, "no address")}",
        UniverseOutputType.EnttecPro => Display(SerialPort, "no COM port chosen"),
        UniverseOutputType.OpenDmx => string.IsNullOrWhiteSpace(FtdiSerialNumber) ? "First FTDI device" : $"Device {FtdiSerialNumber}",
        _ => "Not sent",
    };

    /// <summary>The Port-Address split the way Art-Net nodes show it.</summary>
    public string ArtNetAddressText
    {
        get
        {
            var pa = PortAddressValue;
            return $"Net {pa >> 8} · Sub-Net {(pa >> 4) & 0xF} · Universe {pa & 0xF}";
        }
    }

    public string FrameRateText => $"{FrameRateValue} Hz";

    private int PortAddressValue => (int)Math.Round(ArtNetPortAddress ?? Universe - 1);

    private int SacnUniverseValue => (int)Math.Round(SacnUniverse ?? Universe);

    private int FrameRateValue => (int)Math.Round(FrameRateHz ?? DefaultFrameRate(OutputType));

    /// <summary>Restores a row from a saved configuration (a config may carry several universes).</summary>
    public void Load(OutputConfig config)
    {
        Id = config.Id;
        switch (config)
        {
            case ArtNetOutputConfig artNet:
                OutputType = UniverseOutputType.ArtNet;
                ArtNetDestination = artNet.Destination;
                BindAddress = artNet.BindAddress ?? string.Empty;
                ArtNetPortAddress = (artNet.Net << 8) + (artNet.SubNet << 4) + artNet.Universe + (Universe - 1);
                _artNetUdpPort = artNet.Port;
                break;
            case SacnOutputConfig sacn:
                OutputType = UniverseOutputType.Sacn;
                SacnMulticast = sacn.UseMulticast;
                SacnUnicastAddress = sacn.UnicastAddress ?? string.Empty;
                BindAddress = sacn.BindAddress ?? string.Empty;
                SacnPriority = sacn.Priority;
                SacnSourceName = sacn.SourceName;
                SacnUniverse = Universe + sacn.UniverseOffset;
                _sacnUdpPort = sacn.Port;
                break;
            case EnttecProOutputConfig enttec:
                OutputType = UniverseOutputType.EnttecPro;
                SerialPort = enttec.PortName;
                break;
            case OpenDmxOutputConfig openDmx:
                OutputType = UniverseOutputType.OpenDmx;
                FtdiSerialNumber = openDmx.SerialNumber ?? string.Empty;
                break;
            default:
                OutputType = UniverseOutputType.Virtual;
                break;
        }

        // After the type: changing the type resets the rate to the type's default.
        FrameRateHz = config.FrameRateHz;
    }

    /// <summary>
    /// Builds the output configuration for this universe, or null for "None". <paramref name="localIssue"/> explains
    /// a setting the configuration model cannot express (it blocks enabling).
    /// </summary>
    public OutputConfig? ToConfig(Guid sacnCid, out string? localIssue)
    {
        localIssue = null;
        OutputConfig? config;
        switch (OutputType)
        {
            case UniverseOutputType.Virtual:
                config = new VirtualOutputConfig();
                break;
            case UniverseOutputType.ArtNet:
                // ArtNetOutputConfig maps project universe u to (first Port-Address + u − 1).
                var first = PortAddressValue - (Universe - 1);
                if (first < 0)
                {
                    localIssue = $"{UniverseLabel}: Art-Net universe {PortAddressValue} is not supported yet for this universe " +
                                 $"(the lowest is {Universe - 1}). Patch the fixtures in a lower universe or pick a higher Art-Net universe.";
                    first = 0;
                }

                config = new ArtNetOutputConfig
                {
                    Destination = ArtNetDestination.Trim(),
                    BindAddress = string.IsNullOrWhiteSpace(BindAddress) ? null : BindAddress,
                    Net = first >> 8,
                    SubNet = (first >> 4) & 0xF,
                    Universe = first & 0xF,
                    Port = _artNetUdpPort,
                };
                break;
            case UniverseOutputType.Sacn:
                config = new SacnOutputConfig
                {
                    UseMulticast = SacnMulticast,
                    UnicastAddress = SacnMulticast ? null : SacnUnicastAddress.Trim(),
                    BindAddress = string.IsNullOrWhiteSpace(BindAddress) ? null : BindAddress,
                    Priority = (int)Math.Round(SacnPriority ?? SacnPacket.DefaultPriority),
                    SourceName = SacnSourceName,
                    Cid = sacnCid,
                    UniverseOffset = SacnUniverseValue - Universe,
                    Port = _sacnUdpPort,
                };
                break;
            case UniverseOutputType.EnttecPro:
                config = new EnttecProOutputConfig { PortName = SerialPort.Trim() };
                break;
            case UniverseOutputType.OpenDmx:
                config = new OpenDmxOutputConfig
                {
                    SerialNumber = string.IsNullOrWhiteSpace(FtdiSerialNumber) ? null : FtdiSerialNumber.Trim(),
                };
                break;
            default:
                return null;
        }

        config.Id = Id;
        config.Name = $"{UniverseLabel} · {OutputTypeName}";
        config.Universes = [Universe];
        config.FrameRateHz = FrameRateValue;
        return config;
    }

    /// <summary>Shows the runtime status of this universe's output.</summary>
    public void SetStatus(OutputStatus? status)
    {
        if (status is not { } s || !IsConfigured)
        {
            State = OutputState.Disabled;
            StatusText = IsConfigured ? "Disabled" : "—";
            return;
        }

        State = s.State;
        StatusText = s.State switch
        {
            OutputState.Sending => $"Sending · {s.FramesPerSecond:0} fps",
            OutputState.Connecting => "Connecting…",
            OutputState.Error => $"Error: {s.Message}",
            _ => "Disabled",
        };
    }

    /// <summary>Makes device pickers re-select the saved value after their item lists were refreshed.</summary>
    public void RefreshPickerBindings()
    {
        _suppressConfigurationChanged = true;
        try
        {
            OnPropertyChanged(nameof(BindAddress));
            OnPropertyChanged(nameof(FtdiSerialNumber));
            OnPropertyChanged(nameof(SerialPort));
        }
        finally
        {
            _suppressConfigurationChanged = false;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suppressConfigurationChanged)
        {
            return;
        }

        if (e.PropertyName is null || !ConfigProperties.Contains(e.PropertyName))
        {
            if (e.PropertyName == nameof(IsInProject))
            {
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(UsageText)));
            }

            return;
        }

        if (e.PropertyName == nameof(OutputType))
        {
            FrameRateHz = DefaultFrameRate(OutputType);
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsConfigured)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(OutputTypeName)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TypeDescription)));
            SetStatus(null);
        }

        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ArtNetAddressText)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(FrameRateText)));
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int DefaultFrameRate(UniverseOutputType type) =>
        type == UniverseOutputType.OpenDmx ? OpenDmxOutputConfig.DefaultOpenDmxFrameRateHz : OutputConfig.DefaultFrameRateHz;

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
