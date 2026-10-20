namespace DmxPro.Core.Shows;

/// <summary>
/// The music of a show (one song per show). The file is linked, not embedded (owner decision 2026-10-21): the project
/// keeps the absolute path and a path relative to the project file, so a project moved together with its music folder
/// still finds it (<see cref="ShowAudioPaths"/>). The song starts at show time <see cref="OffsetSeconds"/>.
/// </summary>
public sealed class ShowAudio
{
    /// <summary>Absolute path of the music file when it was linked (or relinked).</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Path relative to the project file's folder (null when the project was never saved or the file is on another drive).</summary>
    public string? RelativePath { get; set; }

    /// <summary>Show time (seconds) at which the song starts. Before it there is no music. May be negative to skip the song's intro.</summary>
    public double OffsetSeconds { get; set; }

    /// <summary>Playback volume 0..1 (default 1).</summary>
    public double Volume { get; set; } = 1;

    /// <summary>Muted: the song does not play, the show still runs.</summary>
    public bool Muted { get; set; }

    /// <summary>Seconds of fade-out before the show ends (0 = none).</summary>
    public double FadeOutSeconds { get; set; }

    /// <summary>Song length in seconds from the last analysis (null until analysed).</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>Tempo detected by the analysis (null until analysed or when no steady beat was found).</summary>
    public double? DetectedBpm { get; set; }

    /// <summary>Deep copy.</summary>
    public ShowAudio Clone() => (ShowAudio)MemberwiseClone();

    /// <summary>
    /// Corrects out-of-range values (hand-edited or damaged file) so a bad file still loads: volume to 0..1 (NaN = 1),
    /// fade-out to &gt;= 0, a non-finite offset to 0, unusable duration/BPM to null, a missing path to empty.
    /// </summary>
    public void Normalize()
    {
        FilePath ??= string.Empty;
        if (string.IsNullOrWhiteSpace(RelativePath))
        {
            RelativePath = null;
        }

        Volume = ShowAudioTiming.ClampVolume(Volume);
        FadeOutSeconds = double.IsFinite(FadeOutSeconds) && FadeOutSeconds > 0 ? FadeOutSeconds : 0;
        OffsetSeconds = double.IsFinite(OffsetSeconds) ? OffsetSeconds : 0;
        if (DurationSeconds is { } duration && !(double.IsFinite(duration) && duration > 0))
        {
            DurationSeconds = null;
        }

        if (DetectedBpm is { } bpm && !(double.IsFinite(bpm) && bpm > 0))
        {
            DetectedBpm = null;
        }
    }
}
