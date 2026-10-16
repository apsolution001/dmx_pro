namespace DmxPro.App.ViewModels;

/// <summary>Placeholder for pages planned in a later roadmap phase.</summary>
public sealed class ComingSoonViewModel : PageViewModel
{
    public ComingSoonViewModel(string title, string subtitle, string phase, IReadOnlyList<string> plannedFeatures)
    {
        Title = title;
        Subtitle = subtitle;
        Phase = phase;
        PlannedFeatures = plannedFeatures;
    }

    public override string Title { get; }

    public override string Subtitle { get; }

    public string Phase { get; }

    public IReadOnlyList<string> PlannedFeatures { get; }
}
