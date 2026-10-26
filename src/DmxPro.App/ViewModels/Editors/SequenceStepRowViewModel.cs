using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One step of the sequence editor's step list (draft step): number, the colour swatches of its lights, optional
/// name and timing. <see cref="IsSelected"/> follows the list's multi-selection (Ctrl/Shift).
/// </summary>
public sealed class SequenceStepRowViewModel : StepTimingRowViewModel
{
    private readonly Action<SequenceStepRowViewModel>? _onSelectionChanged;
    private IReadOnlyList<SequenceSwatch> _swatches = [];
    private int _lightCount;
    private bool _isSelected;

    public SequenceStepRowViewModel(
        SequenceStep step,
        int number,
        Action<StepTimingRowViewModel> onChanged,
        Action<SequenceStepRowViewModel>? onSelectionChanged = null)
        : base(number, onChanged)
    {
        Step = step;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>The draft step (written to the sequence on save).</summary>
    public SequenceStep Step { get; }

    /// <summary>Selected in the step list (several with Ctrl/Shift).</summary>
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

    /// <summary>Label shown in the list; empty = "Step N". Typing a blank name (or "Step N") clears it.</summary>
    public string Name
    {
        get => string.IsNullOrWhiteSpace(Step.Name) ? $"Step {Number}" : Step.Name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) || value.Trim() == $"Step {Number}" ? null : value.Trim();
            if (name != Step.Name)
            {
                Step.Name = name;
                ReportChanged();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(AutomationName));
            OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>"Step 2 · Intro", or just "Step 2" when unnamed: header of the values panel.</summary>
    public string Title => string.IsNullOrWhiteSpace(Step.Name) ? $"Step {Number}" : $"Step {Number} · {Step.Name}";

    /// <summary>The colours of the step's first lights (<see cref="SequenceStepEditing.MaxSwatches"/> at most).</summary>
    public IReadOnlyList<SequenceSwatch> Swatches => _swatches;

    /// <summary>"+12" when the step has more lights than swatches shown, else null.</summary>
    public string? MoreSwatchesText => _lightCount > _swatches.Count ? $"+{_lightCount - _swatches.Count}" : null;

    public string AutomationName => $"Step {Number}: {Name}, fade in {FadeInText}, hold {HoldText}, fade out {FadeOutText} seconds";

    /// <summary>Replaces the swatches (after the step's values or the sequence's lights changed).</summary>
    public void SetSwatches(IReadOnlyList<SequenceSwatch> swatches, int lightCount)
    {
        _swatches = swatches;
        _lightCount = lightCount;
        OnPropertyChanged(nameof(Swatches));
        OnPropertyChanged(nameof(MoreSwatchesText));
    }

    /// <summary>Sets the selection mark without reporting it (the editor rebuilds its list).</summary>
    public void SetSelectedSilently(bool value)
    {
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }

    /// <summary>The step's timing was changed outside the row (Apply timing): re-show the boxes.</summary>
    public void RefreshTiming()
    {
        OnPropertyChanged(nameof(FadeInText));
        OnPropertyChanged(nameof(HoldText));
        OnPropertyChanged(nameof(FadeOutText));
        OnPropertyChanged(nameof(AutomationName));
    }

    protected override double FadeInSeconds
    {
        get => Step.FadeInSeconds;
        set => Step.FadeInSeconds = value;
    }

    protected override double HoldSeconds
    {
        get => Step.HoldSeconds;
        set => Step.HoldSeconds = value;
    }

    protected override double FadeOutSeconds
    {
        get => Step.FadeOutSeconds;
        set => Step.FadeOutSeconds = value;
    }

    protected override void OnNumberChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AutomationName));
    }
}
