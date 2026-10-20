using DmxPro.Audio.Analysis;
using DmxPro.Core.Shows;

namespace DmxPro.App.Services;

/// <summary>
/// The music side of shows: plays each running show's song in sync with its lights (the song is the master clock;
/// the engine's show position follows it), and gives the Shows page what it needs to draw and fix music.
/// Show transport itself stays on <see cref="IConsolePlayback"/> (PlayShow/PauseShow/StopShow/SeekShow): the music
/// follows those calls, wherever they come from (Virtual Console, Shows page, console widgets).
/// </summary>
public interface IShowMusicService
{
    /// <summary>Music state of a show, for status marks in the UI.</summary>
    ShowMusicState GetState(Guid showId);

    /// <summary>A short user-facing message for <see cref="ShowMusicState.Missing"/> / <see cref="ShowMusicState.Error"/>, else null.</summary>
    string? GetStatusMessage(Guid showId);

    /// <summary>Where the show's song is now (see <c>ShowAudioPaths.Resolve</c>), or null (no music / not found).</summary>
    string? ResolvePath(Show show);

    /// <summary>
    /// The song's analysis (waveform, length, tempo), from memory or the disk cache, analysing it in the background when
    /// needed. Null when the show has no music or the file is missing. Errors set the state to Error instead of throwing.
    /// </summary>
    Task<AudioAnalysis?> GetAnalysisAsync(Show show, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Call after the show's music was linked, relinked, removed or its volume/mute/fade/offset changed (the Shows page
    /// does this): reloads or re-applies it, also while the show is playing.
    /// </summary>
    void OnShowAudioChanged(Guid showId);

    /// <summary>
    /// Opens the show's song in the background (a few players are kept ready), so the next Play starts the music
    /// right at the show start instead of joining a moment later. Call when a show becomes the likely next one (the
    /// show in the Virtual Console transport bar, the show open on the Shows page). Cheap when already prepared.
    /// </summary>
    void Preload(Guid showId);

    /// <summary>A show's <see cref="GetState"/> or message changed. Raised on the UI thread.</summary>
    event EventHandler<Guid>? StateChanged;
}
