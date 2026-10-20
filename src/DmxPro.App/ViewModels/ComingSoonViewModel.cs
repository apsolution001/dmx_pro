namespace DmxPro.App.ViewModels;

/// <summary>Placeholder for pages planned in a later roadmap phase.</summary>
public sealed class ComingSoonViewModel : PageViewModel
{
    public ComingSoonViewModel(
        string title,
        string subtitle,
        string phase,
        IReadOnlyList<string> plannedFeatures,
        string iconKind = "Wizard")
    {
        Title = title;
        Subtitle = subtitle;
        Phase = phase;
        PlannedFeatures = plannedFeatures;
        IconKind = iconKind;
    }

    public override string Title { get; }

    public override string Subtitle { get; }

    public string Phase { get; }

    public IReadOnlyList<string> PlannedFeatures { get; }

    /// <summary>Icon name from the app icon set, shown above the title (usually the section's rail icon).</summary>
    public string IconKind { get; }
}
