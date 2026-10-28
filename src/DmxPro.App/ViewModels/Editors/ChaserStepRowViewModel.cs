using System.Globalization;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One step of the chaser editor's step list: what it plays (<see cref="Kind"/>: an own look, the chaser's own copy of
/// a function, an older linked library function, or a deleted one), its colour swatch (on screen only), its timing
/// (draft copy of the step) and whether it is selected (the list allows several). The editor keeps the selection in
/// sync with the list (<see cref="ChaserEditorViewModel.SyncSelection"/>), so the list can virtualize its rows.
/// </summary>
public sealed class ChaserStepRowViewModel : StepTimingRowViewModel
{
    private readonly Action<ChaserStepRowViewModel>? _onSelectionChanged;
    private bool _isSelected;
    private LightFunction? _function;
    private ChaserStepKind _kind;
    private string? _colorHex;

    public ChaserStepRowViewModel(
        ChaserStep step,
        LightFunction? function,
        ChaserStepKind kind,
        int number,
        string? colorHex,
        Action<StepTimingRowViewModel> onChanged,
        Action<ChaserStepRowViewModel>? onSelectionChanged = null)
        : base(number, onChanged)
    {
        ArgumentNullException.ThrowIfNull(step);
        Step = step;
        _function = function;
        _kind = function is null ? ChaserStepKind.Missing : kind;
        _colorHex = colorHex;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>The draft step (a copy; written to the chaser on save).</summary>
    public ChaserStep Step { get; }

    /// <summary>The function the step plays (the editor's draft copy for own steps); null when it no longer exists.</summary>
    public LightFunction? Function => _function;

    public ChaserStepKind Kind => _kind;

    /// <summary>"#RRGGBB" of the light colour the step shows, or null when there is none to show.</summary>
    public string? ColorHex => _colorHex;

    public bool HasColor => ColorHex is not null;

    /// <summary>Selected in the step list.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke(this);
            }
        }
    }

    public bool IsOwnLook => Kind == ChaserStepKind.OwnLook;

    public bool IsFunctionStep => Kind == ChaserStepKind.FunctionStep;

    public bool IsLinked => Kind == ChaserStepKind.Linked;

    public bool IsMissing => Kind == ChaserStepKind.Missing;

    /// <summary>The look of the step can be set right in the chaser (an own look, or a linked scene that becomes one).</summary>
    public bool HasLook => Function is SceneFunction && (IsOwnLook || IsLinked);

    public string FunctionName => Function?.Name ?? "(deleted function)";

    /// <summary>"Own look", "Chaser", "Scene · linked", "deleted, plays nothing".</summary>
    public string TypeName => Kind switch
    {
        ChaserStepKind.Missing => "deleted, plays nothing",
        ChaserStepKind.OwnLook => "Own look",
        ChaserStepKind.Linked => $"{FunctionRowViewModel.TypeDisplayName(Function!.Type)} · linked",
        _ => FunctionRowViewModel.TypeDisplayName(Function!.Type),
    };

    public string IconKind => Function is null ? "Warning" : FunctionRowViewModel.TypeIconKind(Function.Type);

    /// <summary>Tooltip of the name: what the step plays and what editing it does.</summary>
    public string KindToolTip => Kind switch
    {
        ChaserStepKind.Missing => "The function this step played was deleted; the step plays nothing. Remove it or replace it.",
        ChaserStepKind.OwnLook => "Own look: set its lights and colours on the right. It is part of this chaser only.",
        ChaserStepKind.Linked => $"Linked: plays \"{FunctionName}\" from the Functions list. Your first change makes it this chaser's own copy; the original stays unchanged.",
        _ => $"This chaser's own copy of a {FunctionRowViewModel.TypeDisplayName(Function!.Type).ToLowerInvariant()}: Edit step changes it; the original is never changed.",
    };

    /// <summary>Tooltip of the colour swatch.</summary>
    public string ColorToolTip => ColorHex is null
        ? "No colour to show for this step (on screen only)"
        : $"Colour of {FunctionName} (on screen only)";

    /// <summary>"Step 2: Blue · hold 1 s" — tooltip of the step in the colour strip.</summary>
    public string StripToolTip => $"Step {Number}: {FunctionName} · hold {HoldText} s";

    public string AutomationName => $"Step {Number}: {FunctionName}, {TypeName}";

    /// <summary>"3 lights", "4 steps · loop", … — a one-line summary of what the step plays.</summary>
    public string Summary => Describe(Function);

    protected override double FadeInSeconds
    {
        get => Step.FadeInSeconds;
        set => Step.FadeInSeconds = value;
    }

    protected override double HoldSeconds
    {
        get => Step.HoldSeconds;
        set
        {
            Step.HoldSeconds = value;
            OnPropertyChanged(nameof(StripToolTip));
        }
    }

    protected override double FadeOutSeconds
    {
        get => Step.FadeOutSeconds;
        set => Step.FadeOutSeconds = value;
    }

    /// <summary>
    /// The step plays another function now, or its function's look changed (the editor made an own copy, the look was
    /// edited, an inner editor saved it): refresh everything shown for it.
    /// </summary>
    public void Update(LightFunction? function, ChaserStepKind kind, string? colorHex)
    {
        _function = function;
        _kind = function is null ? ChaserStepKind.Missing : kind;
        _colorHex = colorHex;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The step's times were changed from outside (e.g. "Apply to selected"): refresh the cells.</summary>
    public void RefreshTiming()
    {
        OnPropertyChanged(nameof(HoldText));
        OnPropertyChanged(nameof(FadeInText));
        OnPropertyChanged(nameof(FadeOutText));
        OnPropertyChanged(nameof(StripToolTip));
    }

    protected override void OnNumberChanged()
    {
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(StripToolTip));
    }

    private static string Describe(LightFunction? function)
    {
        static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString(CultureInfo.CurrentCulture)} {many}";

        return function switch
        {
            null => "Plays nothing.",
            SceneFunction scene => Count(scene.TargetFixtureIds().Count, "light", "lights"),
            ChaserFunction chaser => $"{Count(chaser.Steps.Count, "step", "steps")} · {EditorTiming.CycleText(chaser.Steps.Count, chaser.CycleSeconds(), chaser.RunOrder)}",
            _ => FunctionRowViewModel.TypeDisplayName(function.Type),
        };
    }
}
