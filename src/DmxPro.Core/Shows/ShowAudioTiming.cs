namespace DmxPro.Core.Shows;

/// <summary>
/// Pure timing math between show time and song time, used by the editor (show length) and the audio sync service
/// (position, volume). Allocation-free; tolerates bad values (NaN, negatives) instead of throwing.
/// </summary>
public static class ShowAudioTiming
{
    /// <summary>Show time (seconds) at which the song ends, or null when the song length is unknown.</summary>
    public static double? SongEndSeconds(ShowAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.DurationSeconds is not { } duration || !double.IsFinite(duration) || duration <= 0)
        {
            return null;
        }

        return SafeOffset(audio.OffsetSeconds) + duration;
    }

    /// <summary>
    /// The show length needed so the whole song fits: the larger of the current length and the song's end
    /// (offset + duration). Without music or a known song length it is the current length.
    /// </summary>
    public static double RequiredLength(Show show)
    {
        ArgumentNullException.ThrowIfNull(show);
        var length = double.IsFinite(show.LengthSeconds) && show.LengthSeconds > 0 ? show.LengthSeconds : 0;
        if (show.Audio is null || SongEndSeconds(show.Audio) is not { } end)
        {
            return length;
        }

        return Math.Max(length, end);
    }

    /// <summary>Song position for a show time: <c>showTime - offset</c> (a non-finite offset counts as 0). May be outside the song.</summary>
    public static double ToSongTime(double showTimeSeconds, double offsetSeconds) => showTimeSeconds - SafeOffset(offsetSeconds);

    /// <summary>Show time for a song position: <c>songTime + offset</c>.</summary>
    public static double ToShowTime(double songTimeSeconds, double offsetSeconds) => songTimeSeconds + SafeOffset(offsetSeconds);

    /// <summary>
    /// Song position at <paramref name="showTimeSeconds"/>. Returns false (silence) before the song starts, at or after
    /// its end (when the length is known) or for a non-finite show time; <paramref name="songTimeSeconds"/> is still the
    /// unclamped position (0 for non-finite input).
    /// </summary>
    public static bool TryGetSongTime(ShowAudio audio, double showTimeSeconds, out double songTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (!double.IsFinite(showTimeSeconds))
        {
            songTimeSeconds = 0;
            return false;
        }

        songTimeSeconds = ToSongTime(showTimeSeconds, audio.OffsetSeconds);
        if (songTimeSeconds < 0)
        {
            return false;
        }

        return audio.DurationSeconds is not { } duration || !double.IsFinite(duration) || songTimeSeconds < duration;
    }

    /// <summary>Volume clamped to 0..1; NaN becomes the default 1.</summary>
    public static double ClampVolume(double volume) => double.IsNaN(volume) ? 1 : Math.Clamp(volume, 0, 1);

    /// <summary>
    /// Fade-out gain 0..1: 1 until <paramref name="fadeOutSeconds"/> before the show end, then linear down to 0 at the
    /// end (and 0 after it). No fade (0, negative, NaN) gives 1 before the end.
    /// </summary>
    public static double FadeOutGain(double showTimeSeconds, double showLengthSeconds, double fadeOutSeconds)
    {
        if (!double.IsFinite(showTimeSeconds) || !double.IsFinite(showLengthSeconds))
        {
            return 1;
        }

        var remaining = showLengthSeconds - showTimeSeconds;
        if (!double.IsFinite(fadeOutSeconds) || fadeOutSeconds <= 0)
        {
            return 1;
        }

        if (remaining <= 0)
        {
            return 0;
        }

        return remaining >= fadeOutSeconds ? 1 : remaining / fadeOutSeconds;
    }

    /// <summary>The volume to play at <paramref name="showTimeSeconds"/>: 0 when muted, else clamped volume × fade-out gain.</summary>
    public static double EffectiveVolume(ShowAudio audio, double showTimeSeconds, double showLengthSeconds)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Muted)
        {
            return 0;
        }

        return ClampVolume(audio.Volume) * FadeOutGain(showTimeSeconds, showLengthSeconds, audio.FadeOutSeconds);
    }

    private static double SafeOffset(double offsetSeconds) => double.IsFinite(offsetSeconds) ? offsetSeconds : 0;
}
