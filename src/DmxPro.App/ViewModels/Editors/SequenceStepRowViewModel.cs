using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>One step of the sequence editor's step grid: optional name and timing (draft step).</summary>
public sealed class SequenceStepRowViewModel : StepTimingRowViewModel
{
    public SequenceStepRowViewModel(SequenceStep step, int number, Action<StepTimingRowViewModel> onChanged)
        : base(number, onChanged)
    {
        Step = step;
    }

    /// <summary>The draft step (written to the sequence on save).</summary>
    public SequenceStep Step { get; }

    /// <summary>Label shown in the grid; empty = "Step N". Typing a blank name (or "Step N") clears it.</summary>
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
        }
    }

    public string AutomationName => $"Step {Number}: {Name}";

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
        OnPropertyChanged(nameof(AutomationName));
    }
}
