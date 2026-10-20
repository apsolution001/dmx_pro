using System.Globalization;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// A row of a step grid (chaser or sequence) with editable Fade in / Hold / Fade out cells. The cells are text: typed
/// numbers are rounded to milliseconds and clamped to 0–<see cref="FunctionEditing.MaxSeconds"/>; anything else
/// reverts to the previous value.
/// </summary>
public abstract class StepTimingRowViewModel : ObservableObject
{
    private readonly Action<StepTimingRowViewModel> _onChanged;
    private int _number;

    protected StepTimingRowViewModel(int number, Action<StepTimingRowViewModel> onChanged)
    {
        _number = number;
        _onChanged = onChanged;
    }

    /// <summary>Position in the list, 1-based.</summary>
    public int Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
            {
                OnNumberChanged();
            }
        }
    }

    public string FadeInText
    {
        get => Format(FadeInSeconds);
        set => SetSeconds(value, FadeInSeconds, v => FadeInSeconds = v);
    }

    public string HoldText
    {
        get => Format(HoldSeconds);
        set => SetSeconds(value, HoldSeconds, v => HoldSeconds = v);
    }

    public string FadeOutText
    {
        get => Format(FadeOutSeconds);
        set => SetSeconds(value, FadeOutSeconds, v => FadeOutSeconds = v);
    }

    protected abstract double FadeInSeconds { get; set; }

    protected abstract double HoldSeconds { get; set; }

    protected abstract double FadeOutSeconds { get; set; }

    /// <summary>"0.5" (current culture, up to 3 decimals).</summary>
    public static string Format(double seconds) => seconds.ToString("0.###", CultureInfo.CurrentCulture);

    protected virtual void OnNumberChanged()
    {
    }

    protected void ReportChanged() => _onChanged(this);

    private void SetSeconds(string text, double current, Action<double> write, [CallerMemberName] string? propertyName = null)
    {
        if (FunctionEditing.TryParseNumber(text, 0, FunctionEditing.MaxSeconds, out var seconds) && seconds != current)
        {
            write(seconds);
            OnPropertyChanged(propertyName);
            _onChanged(this);
            return;
        }

        // Invalid or unchanged: show the stored value again.
        OnPropertyChanged(propertyName);
    }
}
