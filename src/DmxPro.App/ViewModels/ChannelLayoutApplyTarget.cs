namespace DmxPro.App.ViewModels;

/// <summary>
/// "Use it for" choice of the channel layout editor when a built-in profile is copied from the Fixtures page: which
/// patched lights switch to the new layout after saving (none, the selected ones, or all lights of that profile).
/// </summary>
public sealed record ChannelLayoutApplyTarget(string Label, IReadOnlyList<Guid> FixtureIds);
