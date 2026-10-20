using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One function node of the Functions tree. Renamed in place (F2): <see cref="IsEditing"/> swaps the name for a text box bound
/// to <see cref="EditName"/>.
/// </summary>
public sealed partial class FunctionRowViewModel : ObservableObject
{
    private readonly Func<FunctionRowViewModel, string, bool> _rename;
    private readonly Action<FunctionRowViewModel>? _onSelected;

    /// <summary>Tree selection (two-way with the TreeViewItem); selecting reports to the page.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string? _swatch;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    private string? _showBadge;

    [ObservableProperty]
    private bool _isHiddenFromList;

    public FunctionRowViewModel(LightFunction function, Func<FunctionRowViewModel, string, bool> rename, Action<FunctionRowViewModel>? onSelected = null)
    {
        Function = function;
        _rename = rename;
        _onSelected = onSelected;
    }

    public LightFunction Function { get; }

    public string Name => Function.Name;

    public string TypeName => TypeDisplayName(Function.Type);

    /// <summary>Icon kind (<c>DmxIcon.Kind</c>) for the function type.</summary>
    public string IconKind => TypeIconKind(Function.Type);

    public bool IsNotEditing => !IsEditing;

    public bool HasSwatch => Swatch is not null;

    public bool HasShowBadge => ShowBadge is not null;

    public string AutomationName => HasShowBadge ? $"{Name}, {TypeName}, {ShowBadge}" : $"{Name}, {TypeName}";

    public static string TypeDisplayName(FunctionType type) => type switch
    {
        FunctionType.ColorCycle => "Color cycle",
        _ => type.ToString(),
    };

    public static string TypeIconKind(FunctionType type) => type switch
    {
        FunctionType.Scene => "Scenes",
        FunctionType.Chaser => "Chaser",
        FunctionType.Sequence => "Sequence",
        FunctionType.ColorCycle => "ColorCycle",
        FunctionType.Fade => "Fade",
        FunctionType.Pulse => "Pulse",
        FunctionType.Flash => "Flash",
        FunctionType.Strobe => "Strobe",
        _ => "Functions",
    };

    /// <summary>Seconds as a short label, e.g. "0.5 s".</summary>
    public static string FormatSeconds(double seconds) =>
        seconds.ToString("0.##", CultureInfo.CurrentCulture) + " s";

    /// <summary>Re-reads name, swatch, details and badges after the model changed.</summary>
    public void Refresh(Project project)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(AutomationName));
        IsHiddenFromList = Function.OwnerShowId is not null && !Function.ShowInFunctionList;
        ShowBadge = Function.OwnerShowId is { } showId
            ? $"in show: {project.FindShow(showId)?.Name ?? "(deleted show)"}" + (IsHiddenFromList ? " · hidden" : string.Empty)
            : null;

        if (Function is SceneFunction scene)
        {
            var targets = scene.TargetFixtureIds();
            Swatch = targets
                .Select(project.FindFixture)
                .Where(f => f is not null)
                .Select(f => PreviewLight.ToHex(scene.PreviewColor(project, f!)))
                .FirstOrDefault();
            var fixtures = targets.Count == 1 ? "1 fixture" : $"{targets.Count} fixtures";
            Details = $"{fixtures} · fade {FormatSeconds(scene.FadeInSeconds)} / {FormatSeconds(scene.FadeOutSeconds)}";
        }
        else
        {
            Swatch = ShowsLibraryItem.PreviewColor(Function, project)?.ToHex();
            Details = DescribeOther(Function, project);
        }
    }

    /// <summary>One-line summary of a non-scene function ("4 steps · Loop · cycle 8 s").</summary>
    private static string DescribeOther(LightFunction function, Project project)
    {
        static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
        static string Order(RunOrder order) => order switch
        {
            RunOrder.SingleShot => "single shot",
            RunOrder.PingPong => "ping-pong",
            _ => "loop",
        };

        return function switch
        {
            ChaserFunction chaser =>
                $"{Count(chaser.Steps.Count, "step", "steps")} · {Order(chaser.RunOrder)} · {FormatSeconds(chaser.CycleSeconds())}",
            SequenceFunction sequence =>
                $"{Count(sequence.FixtureIds.Count, "fixture", "fixtures")} · {Count(sequence.Steps.Count, "step", "steps")} · {Order(sequence.RunOrder)} · {FormatSeconds(sequence.CycleSeconds())}",
            ColorCycleFunction cycle =>
                $"{Count(cycle.FixtureIds.Count, "fixture", "fixtures")} · {Count(cycle.Colors.Count, "colour", "colours")} · hold {FormatSeconds(cycle.HoldSeconds)} / fade {FormatSeconds(cycle.FadeSeconds)}",
            FadeFunction fade =>
                $"{SceneName(project, fade.FromSceneId, "current state")} → {SceneName(project, fade.ToSceneId, "no target")} · {FormatSeconds(fade.DurationSeconds)}",
            PulseFunction pulse =>
                $"{Count(pulse.FixtureIds.Count, "fixture", "fixtures")} · {(pulse.Color is null ? "dimmer only" : pulse.Shape.ToString().ToLowerInvariant())} · {FormatSeconds(pulse.PeriodSeconds)}",
            FlashFunction flash =>
                $"{Count(flash.FixtureIds.Count, "fixture", "fixtures")} · {(flash.DurationSeconds > 0 ? FormatSeconds(flash.DurationSeconds) : "while held")}",
            StrobeFunction strobe =>
                $"{Count(strobe.FixtureIds.Count, "fixture", "fixtures")} · {(strobe.Mode == StrobeMode.Hardware ? "fixture strobe channel" : strobe.RateHz.ToString("0.##", CultureInfo.CurrentCulture) + " Hz")}",
            _ => string.Empty,
        };
    }

    private static string SceneName(Project project, Guid? id, string none) =>
        id is { } sceneId ? project.FindFunction(sceneId)?.Name ?? "(missing scene)" : none;

    partial void OnSwatchChanged(string? value) => OnPropertyChanged(nameof(HasSwatch));

    partial void OnShowBadgeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasShowBadge));
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected?.Invoke(this);
        }
    }

    /// <summary>Starts inline rename with the current name.</summary>
    public void BeginRename()
    {
        if (IsEditing)
        {
            return;
        }

        EditName = Function.Name;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        if (EditName.Trim() != Function.Name && _rename(this, EditName))
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    [RelayCommand]
    private void CancelRename() => IsEditing = false;
}
