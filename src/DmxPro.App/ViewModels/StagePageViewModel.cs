using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Stage;
using DmxPro.Engine;
using DmxPro.Output;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Stage page: a 2D plan of the stage with every patched light drawn in its live colour and brightness — exactly what
/// the engine sends (after Grand Master, Blackout and strobe safety), also while DMX output is disabled.
/// </summary>
/// <remarks>
/// <para><b>Live preview.</b> While the page is visible a ~30 Hz loop on the UI thread reads the latest
/// <see cref="EngineSnapshot"/>, evaluates each drawable light's <see cref="StageColorModel"/> into
/// <see cref="StageLightViewModel.Current"/> (a preallocated array, no allocations per frame) and raises
/// <see cref="FrameUpdated"/>; the stage canvas then updates its brushes. Polling stops when the page is left.</para>
/// <para><b>Layout edits.</b> Placements live in <see cref="Project.Stage"/> and are saved with the project. They are not
/// read by the engine, so edits mark the project changed with <c>affectsPlayback: false</c>. A mouse drag moves the
/// light live (<see cref="MoveLight"/>) and marks the project changed once on drop (<see cref="CommitMove"/>).</para>
/// <para><b>First open.</b> The first time the page shows a project whose stage has no placements, every drawable light
/// is placed automatically; lights patched later stay in the "not placed" list until placed by the user.</para>
/// </remarks>
public sealed partial class StagePageViewModel : PageViewModel
{
    /// <summary>Snap step of mouse moves and arrow-key nudges, in metres.</summary>
    public const double SnapStep = 0.25;

    /// <summary>Smallest stage width/depth in metres.</summary>
    public const double MinStageSize = StageLayout.MinSize;

    /// <summary>Largest stage width/depth in metres.</summary>
    public const double MaxStageSize = StageLayout.MaxSize;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>Consecutive failed polls before the "live preview stopped" message shows.</summary>
    private const int PollFailuresBeforeWarning = 3;

    private readonly ProjectService _projects;
    private readonly DmxEngine _engine;
    private readonly InputOutputViewModel _inputOutput;
    private readonly ILogger<StagePageViewModel> _logger;
    private readonly long _clockStart = Stopwatch.GetTimestamp();

    /// <summary>Drawable lights (with a colour model), evaluated every frame.</summary>
    private StageLightViewModel[] _drawable = [];

    private Project? _builtProject;
    private long _builtRevision = -1;
    private string? _signature;

    /// <summary>Project instance whose empty stage was already auto-filled (once per opened project).</summary>
    private Project? _autoPlacedFor;

    private bool _isVisible;
    private bool _isMarking;
    private bool _hasPendingMove;
    private CancellationTokenSource? _poll;
    private int _pollFailures;
    private string? _pollError;
    private string? _editError;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private StageLightViewModel? _selectedLight;

    /// <summary>Names under the lights on the stage.</summary>
    [ObservableProperty]
    private bool _showNames = true;

    public StagePageViewModel(
        ProjectService projects,
        DmxEngine engine,
        EngineRuntimeService runtime,
        InputOutputViewModel inputOutput,
        ILogger<StagePageViewModel> logger)
    {
        _projects = projects;
        _engine = engine;
        _inputOutput = inputOutput;
        _logger = logger;

        PlaceUnplacedCommand = new RelayCommand(PlaceUnplaced, () => UnplacedCount > 0);
        ArrangeInLineCommand = new RelayCommand(ArrangeInLine, () => _drawable.Length > 0);
        ArrangeInGridCommand = new RelayCommand<string?>(ArrangeInGrid, _ => _drawable.Length > 0);
        RemoveFromStageCommand = new RelayCommand<StageLightViewModel?>(RemoveFromStage, light => (light ?? SelectedLight) is { IsPlaced: true });
        PlaceOnStageCommand = new RelayCommand<StageLightViewModel?>(PlaceOnStage, light => (light ?? SelectedLight) is { IsDrawable: true, IsPlaced: false });
        FitCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty));

        projects.ContentChanged += (_, _) => OnContentChanged();
        runtime.ProjectLoaded += (_, _) => OnProjectEvent();
        runtime.PatchRefreshed += (_, _) => OnProjectEvent();
        inputOutput.PropertyChanged += OnInputOutputPropertyChanged;
    }

    /// <summary>A new frame was evaluated: the canvas updates the light colours.</summary>
    public event EventHandler? FrameUpdated;

    /// <summary>The set of lights, the stage size or the name labels changed: the canvas rebuilds its drawing.</summary>
    public event EventHandler? SceneChanged;

    /// <summary>Light positions changed (arrange, place, remove, nudge): the canvas repositions the lights.</summary>
    public event EventHandler? PlacementsChanged;

    /// <summary>The selected light changed: the canvas redraws the selection outline.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user asked to zoom the stage to fit the view.</summary>
    public event EventHandler? FitRequested;

    public override string Title => "Stage";

    public override string Subtitle => "See your lights glow live on a plan of the stage.";

    /// <summary>Every patched light in patch order (universe, start address, name).</summary>
    public ObservableCollection<StageLightViewModel> Lights { get; } = [];

    /// <summary><see cref="Lights"/> filtered by <see cref="SearchText"/>.</summary>
    public ObservableCollection<StageLightViewModel> FilteredLights { get; } = [];

    /// <summary>Drawable lights in patch order; the canvas draws the placed ones.</summary>
    public IReadOnlyList<StageLightViewModel> DrawableLights => _drawable;

    public bool HasNoLights => Lights.Count == 0;

    public bool HasNoMatches => Lights.Count > 0 && FilteredLights.Count == 0;

    public int PlacedCount { get; private set; }

    /// <summary>Drawable lights not on the stage yet.</summary>
    public int UnplacedCount { get; private set; }

    /// <summary>"38 lights · 36 on stage · 2 not placed · live".</summary>
    public string StatusText
    {
        get
        {
            var text = new StringBuilder();
            text.Append(Lights.Count).Append(Lights.Count == 1 ? " light" : " lights");
            text.Append(" · ").Append(PlacedCount).Append(" on stage");
            if (UnplacedCount > 0)
            {
                text.Append(" · ").Append(UnplacedCount).Append(" not placed");
            }

            var hidden = Lights.Count - _drawable.Length;
            if (hidden > 0)
            {
                text.Append(" · ").Append(hidden).Append(" not shown");
            }

            if (_poll is not null)
            {
                text.Append(" · live");
            }

            return text.ToString();
        }
    }

    /// <summary>Why the lights on the plan are not (all) reaching the real lights, or null.</summary>
    public string? OutputNote => _inputOutput.State switch
    {
        OutputState.Disabled => "Showing what would be sent — output is disabled",
        OutputState.Error => $"Showing what would be sent — output error: {_inputOutput.StatusMessage ?? _inputOutput.StatusText}",
        _ => null,
    };

    /// <summary>Live preview or editing problem (safety rule 10: shown, not only logged), or null.</summary>
    public string? ErrorMessage => _pollError is { } poll ? $"Live preview stopped updating: {poll}" : _editError;

    /// <summary>Stage width in metres (left–right).</summary>
    public double StageWidth
    {
        get => _projects.Current.Stage.Width;
        set => SetStageSize(value, _projects.Current.Stage.Depth);
    }

    /// <summary>Stage depth in metres (front–back).</summary>
    public double StageDepth
    {
        get => _projects.Current.Stage.Depth;
        set => SetStageSize(_projects.Current.Stage.Width, value);
    }

    public IRelayCommand PlaceUnplacedCommand { get; }

    public IRelayCommand ArrangeInLineCommand { get; }

    /// <summary>Parameter: number of columns, or null/"auto" for a grid that matches the stage shape.</summary>
    public IRelayCommand<string?> ArrangeInGridCommand { get; }

    /// <summary>Takes a light (the parameter, else the selected light) off the stage.</summary>
    public IRelayCommand<StageLightViewModel?> RemoveFromStageCommand { get; }

    /// <summary>Puts a light that is not on the stage (the parameter, else the selected light) in the stage centre.</summary>
    public IRelayCommand<StageLightViewModel?> PlaceOnStageCommand { get; }

    public IRelayCommand FitCommand { get; }

    public override void OnNavigatedTo()
    {
        _isVisible = true;
        RefreshIfChanged();
        TryAutoPlace();
        StartPolling();
    }

    public override void OnNavigatedFrom()
    {
        _isVisible = false;
        StopPolling();
        CommitMove();
    }

    // ───────────────────────────── Canvas API (view-driven edits) ─────────────────────────────

    /// <summary>Moves a light live while it is dragged; the project is marked changed once by <see cref="CommitMove"/>.</summary>
    public void MoveLight(StageLightViewModel light, double x, double y)
    {
        if (!light.IsDrawable || !IsCurrent(light))
        {
            return;
        }

        var placement = _projects.Current.Stage.Place(light.FixtureId, x, y);
        ApplyPlacement(light, placement);
        _hasPendingMove = true;
    }

    /// <summary>Ends a drag: marks the project changed once when a light was moved.</summary>
    public void CommitMove()
    {
        if (_hasPendingMove)
        {
            _hasPendingMove = false;
            MarkStageChanged();
        }
    }

    /// <summary>Ends a drag without saving it (Esc): the light goes back to where it was picked up.</summary>
    public void CancelMove(StageLightViewModel light, double originalX, double originalY)
    {
        if (light.IsDrawable && IsCurrent(light))
        {
            ApplyPlacement(light, _projects.Current.Stage.Place(light.FixtureId, originalX, originalY));
        }

        _hasPendingMove = false;
    }

    /// <summary>Places a light dropped from the lights list at a stage position (metres).</summary>
    public void PlaceLight(Guid fixtureId, double x, double y)
    {
        var light = Find(fixtureId);
        if (light is not { IsDrawable: true })
        {
            return;
        }

        Edit(() =>
        {
            ApplyPlacement(light, _projects.Current.Stage.Place(fixtureId, x, y));
            SelectedLight = light;
        });
    }

    /// <summary>Moves the selected light by the given metres (arrow keys).</summary>
    public void NudgeSelected(double dx, double dy)
    {
        if (SelectedLight is not { IsDrawable: true, IsPlaced: true } light)
        {
            return;
        }

        Edit(() => ApplyPlacement(light, _projects.Current.Stage.Place(light.FixtureId, light.X + dx, light.Y + dy)));
    }

    /// <summary>The light at a fixture id, or null.</summary>
    public StageLightViewModel? Find(Guid fixtureId)
    {
        foreach (var light in Lights)
        {
            if (light.FixtureId == fixtureId)
            {
                return light;
            }
        }

        return null;
    }

    // ───────────────────────────── Commands ─────────────────────────────

    private void PlaceUnplaced() => Edit(() => StageArrange.PlaceUnplaced(_projects.Current.Stage, UnplacedFixtures()));

    private List<Fixture> UnplacedFixtures()
    {
        var fixtures = new List<Fixture>();
        foreach (var light in _drawable)
        {
            if (!light.IsPlaced)
            {
                fixtures.Add(light.Fixture);
            }
        }

        return fixtures;
    }

    private void ArrangeInLine() => Edit(() =>
        StageArrange.ArrangeInLine(_projects.Current.Stage, ArrangeTargets(), _projects.Current.Stage.Depth / 2));

    private void ArrangeInGrid(string? columnsText) => Edit(() =>
    {
        var fixtures = ArrangeTargets();
        if (!int.TryParse(columnsText, out var columns) || columns < 1)
        {
            // Auto: as many columns as make the grid cells roughly square on this stage.
            var stage = _projects.Current.Stage;
            columns = (int)Math.Ceiling(Math.Sqrt(fixtures.Count * stage.Width / Math.Max(stage.Depth, MinStageSize)));
        }

        StageArrange.ArrangeInGrid(_projects.Current.Stage, fixtures, Math.Clamp(columns, 1, Math.Max(fixtures.Count, 1)));
    });

    private void RemoveFromStage(StageLightViewModel? light)
    {
        if ((light ?? SelectedLight) is { IsPlaced: true } target)
        {
            Edit(() => _projects.Current.Stage.Remove(target.FixtureId));
        }
    }

    private void PlaceOnStage(StageLightViewModel? light)
    {
        if ((light ?? SelectedLight) is { IsDrawable: true, IsPlaced: false } target)
        {
            var stage = _projects.Current.Stage;
            PlaceLight(target.FixtureId, stage.Width / 2, stage.Depth / 2);
        }
    }

    /// <summary>
    /// Arrange acts on the lights already on the stage (lights the owner left off stay off); when none is placed yet it
    /// arranges every drawable light.
    /// </summary>
    private List<Fixture> ArrangeTargets()
    {
        var placed = new List<Fixture>();
        var all = new List<Fixture>(_drawable.Length);
        foreach (var light in _drawable)
        {
            all.Add(light.Fixture);
            if (light.IsPlaced)
            {
                placed.Add(light.Fixture);
            }
        }

        return placed.Count > 0 ? placed : all;
    }

    private void SetStageSize(double width, double depth)
    {
        width = Sanitize(width);
        depth = Sanitize(depth);
        var stage = _projects.Current.Stage;
        if (width == stage.Width && depth == stage.Depth)
        {
            return;
        }

        // Resize also pulls every light back onto a smaller stage.
        Edit(() => stage.Resize(width, depth));
        OnPropertyChanged(nameof(StageWidth));
        OnPropertyChanged(nameof(StageDepth));
        SceneChanged?.Invoke(this, EventArgs.Empty);
        FitRequested?.Invoke(this, EventArgs.Empty);

        static double Sanitize(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? MinStageSize : Math.Clamp(Math.Round(value, 2), MinStageSize, MaxStageSize);
    }

    /// <summary>
    /// Runs a layout edit, then syncs the lights and marks the project changed. A failed edit is shown and the lights
    /// re-synced to whatever the layout holds, but the project is not marked changed.
    /// </summary>
    private void Edit(Action edit)
    {
        try
        {
            edit();
            SetEditError(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage layout edit failed");
            SetEditError($"Could not change the stage: {ex.Message}");
            SyncPlacements();
            return;
        }

        SyncPlacements();
        MarkStageChanged();
    }

    private void MarkStageChanged()
    {
        _isMarking = true;
        try
        {
            // The engine never reads the stage layout: no playback recompile.
            _projects.MarkChanged(affectsPlayback: false);
        }
        finally
        {
            _isMarking = false;
        }

        _builtRevision = _projects.Revision;
    }

    private void SetEditError(string? message)
    {
        if (_editError != message)
        {
            _editError = message;
            OnPropertyChanged(nameof(ErrorMessage));
        }
    }

    // ───────────────────────────── Selection / search ─────────────────────────────

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedLightChanged(StageLightViewModel? oldValue, StageLightViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;

            // A light picked on the stage but hidden by the search: clear the search so the list can show it.
            if (SearchText.Length > 0 && !FilteredLights.Contains(newValue))
            {
                SearchText = string.Empty;
            }
        }

        RemoveFromStageCommand.NotifyCanExecuteChanged();
        PlaceOnStageCommand.NotifyCanExecuteChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnShowNamesChanged(bool value) => SceneChanged?.Invoke(this, EventArgs.Empty);

    private void ApplyFilter()
    {
        // Refilling the list makes the bound ListBox drop its selection; keep the stage selection.
        var keep = SelectedLight;
        var search = SearchText.Trim();
        FilteredLights.Clear();
        foreach (var light in Lights)
        {
            if (search.Length == 0 || light.Matches(search))
            {
                FilteredLights.Add(light);
            }
        }

        OnPropertyChanged(nameof(HasNoMatches));
        if (!ReferenceEquals(SelectedLight, keep) && (keep is null || Lights.Contains(keep)))
        {
            SelectedLight = keep;
        }
    }

    // ───────────────────────────── Project events / building ─────────────────────────────

    private void OnContentChanged()
    {
        if (_isMarking)
        {
            _builtRevision = _projects.Revision;
            return;
        }

        if (_isVisible)
        {
            RefreshIfChanged();
            TryAutoPlace();
        }
    }

    private void OnProjectEvent()
    {
        if (_isVisible)
        {
            RefreshIfChanged();
            TryAutoPlace();
        }
    }

    private void OnInputOutputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InputOutputViewModel.State) or nameof(InputOutputViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(OutputNote));
        }
    }

    /// <summary>Rebuilds the lights when the project or its patch changed since the last build.</summary>
    private void RefreshIfChanged()
    {
        var project = _projects.Current;
        if (ReferenceEquals(project, _builtProject) && _builtRevision == _projects.Revision)
        {
            return;
        }

        if (!ReferenceEquals(project, _builtProject))
        {
            // A different project: a pending drag belonged to the old one.
            _hasPendingMove = false;
            Rebuild();
            OnPropertyChanged(nameof(StageWidth));
            OnPropertyChanged(nameof(StageDepth));
            FitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_signature != Signature(project))
        {
            Rebuild();
        }
        else
        {
            // Same patch; a stage edit made elsewhere (e.g. undo later) still needs the positions.
            SyncPlacements();
        }

        _builtRevision = _projects.Revision;
    }

    private void Rebuild()
    {
        var project = _projects.Current;
        _builtProject = project;
        _builtRevision = _projects.Revision;
        _signature = Signature(project);
        var selectedId = SelectedLight?.FixtureId;

        var existing = new HashSet<Guid>();
        foreach (var fixture in project.Fixtures)
        {
            existing.Add(fixture.Id);
        }

        try
        {
            // Placements of deleted lights are dropped silently: they are invisible and meaningless.
            project.Stage.RemoveMissing(existing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Removing stage placements of deleted lights failed");
        }

        var ordered = new List<Fixture>(project.Fixtures);
        ordered.Sort((a, b) =>
        {
            var c = a.Universe.CompareTo(b.Universe);
            if (c == 0)
            {
                c = a.StartAddress.CompareTo(b.StartAddress);
            }

            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        Lights.Clear();
        var drawable = new List<StageLightViewModel>(ordered.Count);
        StageLightViewModel? reselect = null;
        foreach (var fixture in ordered)
        {
            var light = CreateLight(project, fixture);
            Lights.Add(light);
            if (light.IsDrawable)
            {
                drawable.Add(light);
            }

            if (light.FixtureId == selectedId)
            {
                reselect = light;
            }
        }

        _drawable = drawable.ToArray();
        SyncPlacements();
        OnPropertyChanged(nameof(HasNoLights));
        OnPropertyChanged(nameof(DrawableLights));
        ApplyFilter();
        SelectedLight = reselect;
        ArrangeInLineCommand.NotifyCanExecuteChanged();
        ArrangeInGridCommand.NotifyCanExecuteChanged();
        PollTick();
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private StageLightViewModel CreateLight(Project project, Fixture fixture)
    {
        var profile = project.FindProfile(fixture.ProfileId);
        if (profile is null)
        {
            return new(fixture, $"U{fixture.Universe} · {fixture.StartAddress} · missing profile", null, $"its profile \"{fixture.ProfileId}\" is missing");
        }

        var mode = profile.FindMode(fixture.ModeName);
        if (mode is null)
        {
            return new(fixture, $"U{fixture.Universe} · {fixture.StartAddress} · {profile.DisplayName}", null, $"its mode \"{fixture.ModeName}\" is missing");
        }

        var detail = $"U{fixture.Universe} · {fixture.StartAddress} · {profile.DisplayName}";
        if (!fixture.Enabled)
        {
            return new(fixture, detail, null, "it is disabled (sends nothing)");
        }

        if (!DmxAddress.IsValidUniverse(fixture.Universe) || !DmxAddress.IsValid(fixture.StartAddress))
        {
            return new(fixture, detail, null, "its universe or address is invalid");
        }

        StageColorModel model;
        try
        {
            model = StageColorModel.Build(profile, mode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage colour model for {Fixture} failed", fixture.Name);
            return new(fixture, detail, null, $"its look could not be worked out ({ex.Message})");
        }

        if (DmxAddress.EndAddress(fixture.StartAddress, model.ChannelCount) > DmxAddress.Max)
        {
            return new(fixture, detail, null, "it does not fit in its universe");
        }

        return new(fixture, detail, model, null);
    }

    /// <summary>Copies the placements of the layout to the lights and refreshes the counts.</summary>
    private void SyncPlacements()
    {
        var byId = new Dictionary<Guid, StagePlacement>();
        foreach (var placement in _projects.Current.Stage.Placements)
        {
            byId[placement.FixtureId] = placement;
        }

        var placed = 0;
        var unplaced = 0;
        foreach (var light in Lights)
        {
            if (byId.TryGetValue(light.FixtureId, out var placement))
            {
                ApplyPlacement(light, placement);
            }
            else
            {
                light.IsPlaced = false;
            }

            if (light.IsDrawable)
            {
                if (light.IsPlaced)
                {
                    placed++;
                }
                else
                {
                    unplaced++;
                }
            }
        }

        PlacedCount = placed;
        UnplacedCount = unplaced;
        OnPropertyChanged(nameof(PlacedCount));
        OnPropertyChanged(nameof(UnplacedCount));
        OnPropertyChanged(nameof(StatusText));
        PlaceUnplacedCommand.NotifyCanExecuteChanged();
        RemoveFromStageCommand.NotifyCanExecuteChanged();
        PlaceOnStageCommand.NotifyCanExecuteChanged();
        PlacementsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyPlacement(StageLightViewModel light, StagePlacement placement)
    {
        light.X = placement.X;
        light.Y = placement.Y;
        light.Rotation = placement.Rotation;
        light.IsPlaced = true;
    }

    private bool IsCurrent(StageLightViewModel light) =>
        ReferenceEquals(_builtProject, _projects.Current) && Lights.Contains(light);

    /// <summary>
    /// First show of a project whose stage was never arranged and is empty: place every drawable light so the owner
    /// sees them at once. Looking is not editing: the placement stays in memory without marking the project changed
    /// (it is saved with the next real save) and the layout stays "not arranged" (<see cref="StageLayout.IsArranged"/>
    /// reset to false), so a layout the owner deliberately emptied — which sets IsArranged — is never refilled.
    /// </summary>
    private void TryAutoPlace()
    {
        var project = _projects.Current;
        if (ReferenceEquals(_autoPlacedFor, project) || _drawable.Length == 0)
        {
            return;
        }

        _autoPlacedFor = project;
        var stage = project.Stage;
        if (stage.IsArranged || stage.Placements.Count > 0)
        {
            return;
        }

        try
        {
            StageArrange.PlaceUnplaced(stage, UnplacedFixtures());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic stage placement failed");
            SetEditError($"Could not place the lights on the stage: {ex.Message}");
        }
        finally
        {
            // Place() marks the layout arranged; an automatic placement is not the owner's arrangement.
            stage.IsArranged = false;
        }

        SyncPlacements();
        FitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the lights list shows about the patch; a change means the lights must be rebuilt.</summary>
    private static string Signature(Project project)
    {
        var text = new StringBuilder();
        foreach (var fixture in project.Fixtures)
        {
            var profile = project.FindProfile(fixture.ProfileId);
            text.Append(fixture.Id).Append('|').Append(fixture.Name).Append('|').Append(fixture.Universe).Append('|')
                .Append(fixture.StartAddress).Append('|').Append(fixture.ProfileId).Append('|').Append(fixture.ModeName)
                .Append('|').Append(fixture.Enabled);
            if (profile is not null)
            {
                // Profile edits replace the profile object; identity + mode keys catch them.
                text.Append('|').Append(RuntimeHelpers.GetHashCode(profile));
                if (profile.FindMode(fixture.ModeName) is { } mode)
                {
                    text.Append('|').AppendJoin(',', mode.Channels);
                }
            }

            text.Append(';');
        }

        return text.ToString();
    }

    // ───────────────────────────── Polling ─────────────────────────────

    private void StartPolling()
    {
        if (_poll is not null)
        {
            return;
        }

        _poll = new CancellationTokenSource();
        OnPropertyChanged(nameof(StatusText));
        PollTick();
        _ = PollLoopAsync(_poll.Token);
    }

    private void StopPolling()
    {
        _poll?.Cancel();
        _poll?.Dispose();
        _poll = null;
        OnPropertyChanged(nameof(StatusText));
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(token))
            {
                PollTick();
            }
        }
        catch (OperationCanceledException)
        {
            // Page left.
        }
    }

    /// <summary>One poll; a failure is logged once and shown after a few in a row, polling continues.</summary>
    private void PollTick()
    {
        try
        {
            PollOnce();
            if (_pollFailures > 0)
            {
                _pollFailures = 0;
                _pollError = null;
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }
        catch (Exception ex)
        {
            if (_pollFailures++ == 0)
            {
                _logger.LogError(ex, "Stage preview polling failed");
            }

            if (_pollFailures >= PollFailuresBeforeWarning && _pollError is null)
            {
                _pollError = ex.Message;
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }
    }

    /// <summary>Evaluates every drawable light from the latest output frame. Allocation-free.</summary>
    private void PollOnce()
    {
        var snapshot = _engine.GetSnapshot();
        var time = Stopwatch.GetElapsedTime(_clockStart).TotalSeconds;
        var lights = _drawable;
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            var data = snapshot is null ? ReadOnlySpan<byte>.Empty : snapshot.GetUniverse(light.Universe);
            light.Current = light.Model!.Evaluate(data, light.StartAddress, time);
        }

        FrameUpdated?.Invoke(this, EventArgs.Empty);
    }
}
