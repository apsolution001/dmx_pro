using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One step of the chaser editor's step list: the function it plays, its colour swatch (on screen only), its timing
/// (draft copy of the step) and whether it is selected (the list allows several).
/// </summary>
public sealed class ChaserStepRowViewModel : StepTimingRowViewModel
{
    private readonly Action<ChaserStepRowViewModel>? _onSelectionChanged;
    private bool _isSelected;

    public ChaserStepRowViewModel(
        ChaserStep step,
        LightFunction? function,
        int number,
        string? colorHex,
        Action<StepTimingRowViewModel> onChanged,
        Action<ChaserStepRowViewModel>? onSelectionChanged = null)
        : base(number, onChanged)
    {
        Step = step;
        Function = function;
        ColorHex = colorHex;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>The draft step (a copy; written to the chaser on save).</summary>
    public ChaserStep Step { get; }

    /// <summary>The function the step plays; null when it no longer exists.</summary>
    public LightFunction? Function { get; }

    /// <summary>"#RRGGBB" of the light colour the step's function shows, or null when there is none to show.</summary>
    public string? ColorHex { get; }

    public bool HasColor => ColorHex is not null;

    /// <summary>Selected in the step list (two-way with the list row).</summary>
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

    public bool IsMissing => Function is null;

    public string FunctionName => Function?.Name ?? "(missing function)";

    public string TypeName => Function is null ? "deleted, plays nothing" : FunctionRowViewModel.TypeDisplayName(Function.Type);

    public string IconKind => Function is null ? "Warning" : FunctionRowViewModel.TypeIconKind(Function.Type);

    /// <summary>Tooltip of the colour swatch.</summary>
    public string ColorToolTip => ColorHex is null
        ? "No colour to show for this step (on screen only)"
        : $"Colour of {FunctionName} (on screen only)";

    /// <summary>"Step 2: Blue · hold 1 s" — tooltip of the step in the colour strip.</summary>
    public string StripToolTip => $"Step {Number}: {FunctionName} · hold {HoldText} s";

    public string AutomationName => $"Step {Number}: {FunctionName}, {TypeName}";

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
}
