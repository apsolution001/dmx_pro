namespace DmxPro.App.ViewModels;

/// <summary>
/// One faint item block on the console's show timeline (orientation only, never edited there). All values are
/// fractions 0..1 of the timeline: <see cref="Left"/>/<see cref="Width"/> of its width (time), <see cref="Top"/>/
/// <see cref="Height"/> of its height (track row).
/// </summary>
public sealed record ShowTimelineBlock(double Left, double Width, double Top, double Height);
