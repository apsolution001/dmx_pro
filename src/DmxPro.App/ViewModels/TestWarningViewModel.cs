namespace DmxPro.App.ViewModels;

/// <summary>One entry of the Test &amp; Preview warnings strip. Errors are shown red, warnings amber.</summary>
public sealed class TestWarningViewModel
{
    public TestWarningViewModel(string message, string? detail, bool isError)
    {
        Message = message;
        Detail = detail;
        IsError = isError;
    }

    public string Message { get; }

    /// <summary>Longer explanation or the affected lights (tooltip).</summary>
    public string? Detail { get; }

    /// <summary>Red (blocks output/testing) instead of amber.</summary>
    public bool IsError { get; }

    /// <summary>Same text and severity (used to skip needless list rebuilds while polling).</summary>
    public bool SameAs(TestWarningViewModel other) =>
        IsError == other.IsError && Message == other.Message && Detail == other.Detail;
}
