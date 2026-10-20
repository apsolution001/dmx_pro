using DmxPro.Audio.Playback;
using DmxPro.Core.Shows;

namespace DmxPro.App.Services;

/// <summary>
/// Music state of one running show inside <see cref="ShowMusicService"/>. Plain data; only touched on the UI thread.
/// </summary>
internal sealed class ShowMusicSession(Show show)
{
    public Guid ShowId { get; } = show.Id;

    /// <summary>The project's show (refreshed after edits).</summary>
    public Show Show { get; set; } = show;

    /// <summary>The player, null while there is none (missing file, load failed, playback error).</summary>
    public IAudioPlayer? Player { get; set; }

    /// <summary>Resolved file the player is for.</summary>
    public string? Path { get; set; }

    /// <summary>Cancels the running load; also identifies it (a finished load of an old CTS is ignored).</summary>
    public CancellationTokenSource? LoadCancellation { get; set; }

    /// <summary>The player's <see cref="IAudioPlayer.LoadAsync"/> task.</summary>
    public Task? LoadTask { get; set; }

    /// <summary>The player has the song open and ready.</summary>
    public bool Loaded { get; set; }

    /// <summary>Song length from the player.</summary>
    public double SongDurationSeconds { get; set; }

    /// <summary>Song start offset the audio is currently joined with.</summary>
    public double OffsetSeconds { get; set; }

    /// <summary>The engine plays the show (running and not paused).</summary>
    public bool EnginePlaying { get; set; }

    /// <summary>Play() was called on the player and not paused since.</summary>
    public bool AudioPlaying { get; set; }

    /// <summary>The music must (re)join the show at the next chance.</summary>
    public bool NeedsJoin { get; set; }

    /// <summary>Scrub / fast-seek holds: music silent while &gt; 0.</summary>
    public int HoldCount { get; set; }

    /// <summary>Fail-safe: a hold nobody ended expires at this time.</summary>
    public double HoldExpires { get; set; }

    /// <summary>Rapid seeking: music stays silent until this time.</summary>
    public double SettleUntil { get; set; }

    /// <summary>Time of the last user seek (to detect rapid seeking).</summary>
    public double LastSeekTime { get; set; } = double.NegativeInfinity;

    /// <summary>Waiting for the first sample after Play/Seek to be heard.</summary>
    public bool WarmingUp { get; set; }

    /// <summary>When the last join called Play/Seek.</summary>
    public double JoinTime { get; set; }

    /// <summary>Song position the last join sought to.</summary>
    public double JoinAudioPosition { get; set; }

    /// <summary>The last join started from silence, so the time until it was heard measures the output latency.</summary>
    public bool MeasureLatency { get; set; }

    /// <summary>
    /// The song starts at the very beginning of the show, before the lights could wait for the output latency: hold the
    /// lights at the start until the first sample is heard.
    /// </summary>
    public bool HoldLightsUntilHeard { get; set; }

    /// <summary>Light show time to hold at while <see cref="HoldLightsUntilHeard"/>.</summary>
    public double HoldLightsAt { get; set; }

    public double LastAudioPosition { get; set; }

    public double LastAudioAdvanceTime { get; set; }

    /// <summary>Earliest time of the next drift correction.</summary>
    public double NextCorrectionTime { get; set; }

    public ShowMusicClock Clock { get; } = new();

    public ShowMusicDriftFilter Drift { get; } = new();
}
