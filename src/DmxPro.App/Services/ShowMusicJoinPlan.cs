namespace DmxPro.App.Services;

/// <summary>Result of <see cref="ShowMusicSync.PlanJoin"/>.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="AudioPositionSeconds">Song position to seek the player to before Play.</param>
/// <param name="EngineShowTimeSeconds">
/// For <see cref="ShowMusicJoinKind.StartAtSongStart"/>: the show time the lights must be at now, so that they reach the
/// song start exactly when its first sample is heard (may be negative: hold the lights at 0 until the music is heard).
/// </param>
internal readonly record struct ShowMusicJoinPlan(ShowMusicJoinKind Kind, double AudioPositionSeconds = 0, double EngineShowTimeSeconds = 0);
