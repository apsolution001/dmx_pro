using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Patching;
using DmxPro.Core.Validation;
using DmxPro.Output;
using DmxPro.Output.Configuration;
using DmxPro.Output.Transport;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Input / Output page: which output each project universe is sent through, and the runtime "output enabled" switch.
/// </summary>
/// <remarks>
/// Safety: output starts disabled (rule 1) — this page never enables it on its own, and the enabled state is never
/// saved. Enabling validates first and shows every problem (rules 2, 10). Disabling stops transmission; it is not
/// blackout (rule 3), so there is no blackout control here. Output configurations are app-level settings
/// (<see cref="AppSettings.Outputs"/>), because the project file has no output section yet.
/// </remarks>
public sealed partial class InputOutputViewModel : PageViewModel
{
    private readonly OutputManager _output;
    private readonly AppSettingsService _settings;
    private readonly ProjectService _projects;
    private readonly ILogger<InputOutputViewModel> _logger;
    private readonly SynchronizationContext? _ui;
    private readonly HashSet<int> _addedUniverses = [];
    private IReadOnlyList<OutputUniverseRowViewModel> _managerRows = [];
    private IReadOnlyList<IDmxOutput> _subscribedOutputs = [];
    private int _statusUpdateQueued;
    private bool _loading;
    private bool _discoveredOnce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(ClearOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveUniverseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseBroadcastCommand))]
    private OutputUniverseRowViewModel? _selectedRow;

    [ObservableProperty]
    private OutputState _state = OutputState.Disabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableButtonText))]
    [NotifyPropertyChangedFor(nameof(EnableButtonToolTip))]
    [NotifyCanExecuteChangedFor(nameof(EnableOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyChangesCommand))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyChangesCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "DMX output disabled";

    /// <summary>Error detail of the aggregate status (null unless in Error).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Settings were edited while output is enabled; they apply on the next enable (or "Apply now").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyChangesCommand))]
    private bool _hasPendingChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    private bool _isDiscovering;

    [ObservableProperty]
    private string? _discoveryText;

    /// <summary>FTDI driver missing etc. Shown under the Open DMX device picker.</summary>
    [ObservableProperty]
    private string? _ftdiMessage;

    /// <summary>Headline above the problem list ("Output not enabled — fix these first").</summary>
    [ObservableProperty]
    private string? _issuesTitle;

    [ObservableProperty]
    private bool _issuesAreErrors;

    public InputOutputViewModel(
        OutputManager output,
        AppSettingsService settings,
        ProjectService projects,
        ILogger<InputOutputViewModel> logger)
    {
        _output = output;
        _settings = settings;
        _projects = projects;
        _logger = logger;
        _ui = SynchronizationContext.Current;

        Adapters.Add(NetworkAdapterDiscovery.AnyAdapter);
        LoadRows();

        _output.StatusChanged += OnOutputStatusChanged;
        _projects.ContentChanged += (_, _) => SyncProjectUniverses();
        _projects.ProjectChanged += async (_, _) => await OnProjectChangedAsync();

        // Lets other entry points (Output menu, status bar) enable with the saved configuration too.
        _ = PushConfigurationsAsync();
        ApplyStatus(_output.Status);
    }

    public override string Title => "Input / Output";

    public override string Subtitle => "Send DMX to Art-Net, sACN or USB-DMX interfaces.";

    public ObservableCollection<OutputUniverseRowViewModel> Rows { get; } = [];

    public IReadOnlyList<OutputTypeOption> OutputTypes => OutputTypeOption.All;

    public ObservableCollection<NetworkAdapterOption> Adapters { get; } = [];

    public ObservableCollection<string> SerialPorts { get; } = [];

    public ObservableCollection<FtdiDeviceOption> FtdiDevices { get; } = [];

    public ObservableCollection<ValidationIssue> Issues { get; } = [];

    public bool HasSelection => SelectedRow is not null;

    public string EnableButtonText => IsEnabled ? "Disable output" : "Enable output";

    public string EnableButtonToolTip => IsEnabled
        ? "Stop sending DMX (not blackout: lights keep their last state on most devices)"
        : "Check the configuration and start sending DMX to the configured outputs";

    public override void OnNavigatedTo()
    {
        if (!_discoveredOnce)
        {
            _discoveredOnce = true;
            RefreshDevicesCommand.Execute(null);
        }
    }

    // ───────────────────────── Enable / disable ─────────────────────────

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private Task ToggleOutputAsync() => IsEnabled ? DisableOutputAsync() : EnableOutputAsync();

    private bool CanToggle() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private async Task EnableOutputAsync()
    {
        IsBusy = true;
        try
        {
            var (configs, rows, issues) = BuildConfigurations();
            issues.AddRange(OutputManager.Validate(configs));
            if (HasErrors(issues))
            {
                ShowIssues(issues, "Output not enabled — fix these problems first:");
                return;
            }

            await _output.SetConfigurationsAsync(configs);
            _managerRows = rows;
            var result = await _output.EnableAsync();
            var all = issues.Concat(result).Distinct().ToList();
            ShowIssues(all, HasErrors(all) ? "Output not enabled — fix these problems first:" : "Output enabled with warnings:");
            HasPendingChanges = false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Enabling output failed: {Error}", ex.Message);
            ShowIssues([ValidationIssue.Error("OUTPUT_ENABLE_FAILED", ex.Message)], "Output could not be enabled:");
        }
        finally
        {
            IsBusy = false;
            ApplyStatus(_output.Status);
        }
    }

    private bool CanEnable() => !IsBusy && !IsEnabled;

    /// <summary>Stops transmission. Not blackout: blackout is a separate control in the status bar (safety rule 3).</summary>
    [RelayCommand(CanExecute = nameof(CanDisable))]
    private async Task DisableOutputAsync()
    {
        IsBusy = true;
        try
        {
            await _output.DisableAsync();
            HasPendingChanges = false;
        }
        finally
        {
            IsBusy = false;
            ApplyStatus(_output.Status);
            await PushConfigurationsAsync();
        }
    }

    private bool CanDisable() => !IsBusy && IsEnabled;

    /// <summary>Restarts output with the edited settings (disable → apply → enable), only on the user's request.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyChanges))]
    private async Task ApplyChangesAsync()
    {
        await DisableOutputAsync();
        await EnableOutputAsync();
    }

    private bool CanApplyChanges() => !IsBusy && IsEnabled && HasPendingChanges;

    /// <summary>Validates without enabling, so the user can fix problems before the show.</summary>
    [RelayCommand]
    private void CheckConfiguration()
    {
        var (configs, _, issues) = BuildConfigurations();
        issues.AddRange(OutputManager.Validate(configs));
        if (issues.Count == 0)
        {
            ShowIssues([], null);
            IssuesTitle = $"Configuration OK: {configs.Count} output(s) ready to enable.";
            IssuesAreErrors = false;
            return;
        }

        ShowIssues(issues, HasErrors(issues) ? "These problems block enabling output:" : "Warnings:");
    }

    // ───────────────────────── Universes ─────────────────────────

    [RelayCommand]
    private void AddUniverse()
    {
        var next = Rows.Count == 0 ? 1 : Rows.Max(r => r.Universe) + 1;
        if (!DmxAddress.IsValidUniverse(next))
        {
            return;
        }

        _addedUniverses.Add(next);
        var row = CreateRow(next, isInProject: false, config: null);
        Rows.Add(row);
        SelectedRow = row;
    }

    /// <summary>Removes a universe that has no fixtures (its output setting is dropped).</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveUniverse))]
    private void RemoveUniverse()
    {
        if (SelectedRow is not { IsInProject: false } row)
        {
            return;
        }

        _addedUniverses.Remove(row.Universe);
        row.ConfigurationChanged -= OnRowConfigurationChanged;
        Rows.Remove(row);
        SelectedRow = Rows.FirstOrDefault();
        OnConfigurationEdited();
    }

    private bool CanRemoveUniverse() => SelectedRow is { IsInProject: false };

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearOutput()
    {
        if (SelectedRow is { } row)
        {
            row.OutputType = UniverseOutputType.None;
        }
    }

    /// <summary>Art-Net: send to the broadcast address of the chosen adapter (or 255.255.255.255).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void UseBroadcast()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var adapter = Adapters.FirstOrDefault(a => a.Address == row.BindAddress);
        row.ArtNetDestination = adapter?.Broadcast ?? ArtNetOutputConfig.LimitedBroadcast;
    }

    // ───────────────────────── Device discovery ─────────────────────────

    /// <summary>Lists adapters, COM ports and FTDI devices. Listing never opens a port or sends anything.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshDevices))]
    private async Task RefreshDevicesAsync()
    {
        IsDiscovering = true;
        try
        {
            var (adapters, ports, ftdiOk, ftdi, ftdiError) = await Task.Run(() =>
            {
                var a = NetworkAdapterDiscovery.GetIPv4Adapters();
                var p = SerialPortDiscovery.GetPortNames();
                var ok = FtdiDiscovery.TryListDevices(out var f, out var error);
                return (a, p, ok, f, error);
            });

            // Values saved in a row but not found now stay selectable ("not found"), so a refresh never
            // silently rewrites a saved setting through the pickers' SelectedValue bindings.
            var adapterList = adapters.ToList();
            foreach (var missing in Rows.Select(r => r.BindAddress).Where(a => !string.IsNullOrWhiteSpace(a))
                         .Distinct().Where(a => adapterList.All(o => o.Address != a)).ToList())
            {
                adapterList.Add(new NetworkAdapterOption(missing, $"{missing} (adapter not found)", null));
            }

            var ftdiList = ftdi.Select(d => new FtdiDeviceOption(
                d.SerialNumber,
                $"{(string.IsNullOrWhiteSpace(d.Description) ? "FTDI device" : d.Description)} ({d.SerialNumber})" +
                (d.IsOpen ? " — in use by another program" : string.Empty))).ToList();
            foreach (var missing in Rows.Select(r => r.FtdiSerialNumber).Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct().Where(s => ftdiList.All(o => o.SerialNumber != s)).ToList())
            {
                ftdiList.Add(new FtdiDeviceOption(missing, $"{missing} (not connected)"));
            }

            Sync(Adapters, adapterList);
            Sync(SerialPorts, ports);
            Sync(FtdiDevices, ftdiList);
            foreach (var row in Rows)
            {
                row.RefreshPickerBindings();
            }

            FtdiMessage = ftdiOk
                ? (ftdi.Count == 0 ? "No FTDI device found. Plug in the interface and click Refresh." : null)
                : ftdiError;
            DiscoveryText = $"Found {Count(adapters.Count - 1, "network adapter", "network adapters")} · " +
                            $"{Count(ports.Count, "COM port", "COM ports")} · " +
                            (ftdiOk ? Count(ftdi.Count, "FTDI device", "FTDI devices") : "FTDI driver not available");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning("Device discovery failed: {Error}", ex.Message);
            DiscoveryText = $"Could not list devices: {ex.Message}";
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    private bool CanRefreshDevices() => !IsDiscovering;

    // ───────────────────────── Rows and persistence ─────────────────────────

    private void LoadRows()
    {
        _loading = true;
        try
        {
            var projectUniverses = _projects.Current.Universes.ToHashSet();
            var configured = new Dictionary<int, OutputConfig>();
            foreach (var config in _settings.Current.Outputs)
            {
                foreach (var universe in config.Universes ?? [])
                {
                    if (DmxAddress.IsValidUniverse(universe))
                    {
                        configured.TryAdd(universe, config);
                    }
                }
            }

            foreach (var universe in projectUniverses.Union(configured.Keys).Order())
            {
                Rows.Add(CreateRow(universe, projectUniverses.Contains(universe), configured.GetValueOrDefault(universe)));
            }

            // Saved picks stay selectable until the first device refresh replaces them with real entries.
            foreach (var address in Rows.Select(r => r.BindAddress).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
            {
                Adapters.Add(new NetworkAdapterOption(address, address, null));
            }

            foreach (var serial in Rows.Select(r => r.FtdiSerialNumber).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
            {
                FtdiDevices.Add(new FtdiDeviceOption(serial, serial));
            }

            SelectedRow = Rows.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }
    }

    private OutputUniverseRowViewModel CreateRow(int universe, bool isInProject, OutputConfig? config)
    {
        var row = new OutputUniverseRowViewModel(universe, isInProject);
        if (config is not null)
        {
            row.Load(config);
        }

        row.SetStatus(null);
        row.ConfigurationChanged += OnRowConfigurationChanged;
        return row;
    }

    /// <summary>Adds rows for universes that got fixtures; drops unused, unconfigured rows.</summary>
    private void SyncProjectUniverses()
    {
        var projectUniverses = _projects.Current.Universes.ToHashSet();
        foreach (var row in Rows.ToList())
        {
            row.IsInProject = projectUniverses.Contains(row.Universe);
            if (!row.IsInProject && !row.IsConfigured && !_addedUniverses.Contains(row.Universe))
            {
                row.ConfigurationChanged -= OnRowConfigurationChanged;
                Rows.Remove(row);
            }
        }

        foreach (var universe in projectUniverses.Where(u => Rows.All(r => r.Universe != u)).Order())
        {
            var index = 0;
            while (index < Rows.Count && Rows[index].Universe < universe)
            {
                index++;
            }

            Rows.Insert(index, CreateRow(universe, isInProject: true, config: null));
        }

        SelectedRow ??= Rows.FirstOrDefault();
        RemoveUniverseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Safety rule 1: output is disabled whenever a project is opened or created.</summary>
    private async Task OnProjectChangedAsync()
    {
        SyncProjectUniverses();
        if (_output.IsEnabled && !IsBusy)
        {
            await DisableOutputAsync();
            ShowIssues(
                [ValidationIssue.Warning("OUTPUT_DISABLED_ON_OPEN", "DMX output was disabled because a project was opened. Enable it again when ready.")],
                "Output disabled:");
        }
    }

    private void OnRowConfigurationChanged(object? sender, EventArgs e)
    {
        if (sender is OutputUniverseRowViewModel row)
        {
            row.IssueText = null;
        }

        if (!_loading)
        {
            OnConfigurationEdited();
        }
    }

    private void OnConfigurationEdited()
    {
        var (configs, _, _) = BuildConfigurations();
        if (!_settings.Update(s => s.Outputs = configs))
        {
            ShowIssues([ValidationIssue.Error("SETTINGS_SAVE_FAILED", _settings.SaveError ?? "Settings could not be saved.")], "Output settings not saved:");
        }

        if (IsEnabled || IsBusy)
        {
            HasPendingChanges = true;
        }
        else
        {
            _ = PushConfigurationsAsync();
        }
    }

    /// <summary>Hands the saved configuration to the manager while output is disabled (never enables it).</summary>
    private async Task PushConfigurationsAsync()
    {
        if (_output.IsEnabled || IsBusy)
        {
            return;
        }

        try
        {
            var (configs, rows, _) = BuildConfigurations();
            await _output.SetConfigurationsAsync(configs);
            _managerRows = rows;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _logger.LogWarning("Could not hand the output configuration to the output manager: {Error}", ex.Message);
        }
    }

    private (List<OutputConfig> Configs, List<OutputUniverseRowViewModel> Rows, List<ValidationIssue> Issues) BuildConfigurations()
    {
        var configs = new List<OutputConfig>();
        var rows = new List<OutputUniverseRowViewModel>();
        var issues = new List<ValidationIssue>();
        foreach (var row in Rows)
        {
            var config = row.ToConfig(_settings.Current.SacnCid, out var localIssue);
            if (config is null)
            {
                continue;
            }

            configs.Add(config);
            rows.Add(row);
            if (localIssue is not null)
            {
                issues.Add(ValidationIssue.Error("OUTPUT_ARTNET_UNIVERSE_UNSUPPORTED", localIssue, row.Id));
            }
        }

        return (configs, rows, issues);
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues, string? title)
    {
        Replace(Issues, issues);
        IssuesTitle = issues.Count == 0 ? null : title;
        IssuesAreErrors = HasErrors(issues);
        foreach (var row in Rows)
        {
            var mine = issues.Where(i => i.EntityId == row.Id).ToList();
            row.IssueText = mine.Count == 0 ? null : string.Join("\n", mine.Select(i => i.Message));
            row.IssueIsError = HasErrors(mine);
        }
    }

    // ───────────────────────── Status (background threads → UI) ─────────────────────────

    private void OnOutputStatusChanged(object? sender, OutputStatus status) => QueueStatusUpdate();

    private void QueueStatusUpdate()
    {
        // Coalesce bursts (fps updates from several outputs) into one UI update.
        if (Interlocked.Exchange(ref _statusUpdateQueued, 1) == 1)
        {
            return;
        }

        if (_ui is null)
        {
            Interlocked.Exchange(ref _statusUpdateQueued, 0);
            ApplyStatus(_output.Status);
            return;
        }

        _ui.Post(
            _ =>
            {
                Interlocked.Exchange(ref _statusUpdateQueued, 0);
                ApplyStatus(_output.Status);
            },
            null);
    }

    private void ApplyStatus(OutputStatus status)
    {
        IsEnabled = _output.IsEnabled;
        State = status.State;
        StatusMessage = status.State == OutputState.Error ? status.Message : null;
        StatusText = status.State switch
        {
            OutputState.Sending => $"DMX output sending · {status.FramesPerSecond:0} fps",
            OutputState.Connecting => "DMX output connecting…",
            OutputState.Error => "DMX output error",
            _ => "DMX output disabled",
        };

        ResubscribeOutputs();
        var outputs = _output.ActiveOutputs;
        var active = new Dictionary<OutputUniverseRowViewModel, OutputStatus>();
        for (var i = 0; i < outputs.Count && i < _managerRows.Count; i++)
        {
            active[_managerRows[i]] = outputs[i].Status;
        }

        foreach (var row in Rows)
        {
            row.SetStatus(active.TryGetValue(row, out var s) ? s : null);
        }
    }

    /// <summary>Per-output events give each row its own status (the aggregate only names the worst one).</summary>
    private void ResubscribeOutputs()
    {
        var outputs = _output.ActiveOutputs;
        if (ReferenceEquals(outputs, _subscribedOutputs))
        {
            return;
        }

        foreach (var old in _subscribedOutputs)
        {
            old.StatusChanged -= OnOutputStatusChanged;
        }

        foreach (var output in outputs)
        {
            output.StatusChanged += OnOutputStatusChanged;
        }

        _subscribedOutputs = outputs;
    }

    private static bool HasErrors(IEnumerable<ValidationIssue> issues) =>
        issues.Any(i => i.Severity == ValidationSeverity.Error);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    /// <summary>Updates a picker list in place (no Clear), so bound selections are not reset.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!items.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var existing = target.IndexOf(items[i]);
            if (existing < 0)
            {
                target.Insert(Math.Min(i, target.Count), items[i]);
            }
            else if (existing != i && i < target.Count)
            {
                target.Move(existing, i);
            }
        }
    }

    private static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}
