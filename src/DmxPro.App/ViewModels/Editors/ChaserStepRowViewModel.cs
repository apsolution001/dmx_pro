using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>One step of the chaser editor's step grid: the function it plays and its timing (draft copy of the step).</summary>
public sealed class ChaserStepRowViewModel : StepTimingRowViewModel
{
    public ChaserStepRowViewModel(ChaserStep step, LightFunction? function, int number, Action<StepTimingRowViewModel> onChanged)
        : base(number, onChanged)
    {
        Step = step;
        Function = function;
    }

    /// <summary>The draft step (a copy; written to the chaser on save).</summary>
    public ChaserStep Step { get; }

    /// <summary>The function the step plays; null when it no longer exists.</summary>
    public LightFunction? Function { get; }

    public bool IsMissing => Function is null;

    public string FunctionName => Function?.Name ?? "(missing function)";

    public string TypeName => Function is null ? "deleted, plays nothing" : FunctionRowViewModel.TypeDisplayName(Function.Type);

    public string IconKind => Function is null ? "Warning" : FunctionRowViewModel.TypeIconKind(Function.Type);

    public string AutomationName => $"Step {Number}: {FunctionName}, {TypeName}";

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

    protected override void OnNumberChanged() => OnPropertyChanged(nameof(AutomationName));
}
