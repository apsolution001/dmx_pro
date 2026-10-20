namespace DmxPro.Engine;

/// <summary>A show that was playing (or paused) when a snapshot was taken.</summary>
/// <param name="ShowId">Id of the show.</param>
/// <param name="Position">Current playback position.</param>
/// <param name="Length">Length of the show; playback stops when the position reaches it.</param>
/// <param name="IsPaused">True while paused: the position is frozen and the show's lights hold their look.</param>
/// <param name="IsMuted">
/// True while the show's light output is muted by an active console function
/// (<see cref="ShowOverrideMode.MuteWholeShow"/>); its timeline keeps running.
/// </param>
public readonly record struct ShowPlaybackInfo(Guid ShowId, TimeSpan Position, TimeSpan Length, bool IsPaused = false, bool IsMuted = false);
