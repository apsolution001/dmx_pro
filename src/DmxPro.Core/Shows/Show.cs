using System.Text.Json.Serialization;

namespace DmxPro.Core.Shows;

/// <summary>A timeline: tracks holding function items placed in time, and optionally one song (<see cref="Audio"/>).</summary>
public sealed class Show
{
    public const double DefaultLengthSeconds = 180;
    public const double DefaultBpm = 120;

    /// <summary>Slowest tempo the editor accepts.</summary>
    public const double MinBpm = 20;

    /// <summary>Fastest tempo the editor accepts.</summary>
    public const double MaxBpm = 300;

    /// <summary>Longest show the editor accepts (24 h).</summary>
    public const double MaxLengthSeconds = 24 * 3600;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public double LengthSeconds { get; set; } = DefaultLengthSeconds;

    /// <summary>Tempo used for snapping items to beats in the editor.</summary>
    public double Bpm { get; set; } = DefaultBpm;

    public List<ShowTrack> Tracks { get; set; } = [];

    /// <summary>Time of the first beat (seconds): beat and bar lines and snapping are counted from here (from the music analysis).</summary>
    public double BeatOffsetSeconds { get; set; }

    /// <summary>The show's music, or null for a show without music.</summary>
    public ShowAudio? Audio { get; set; }

    /// <summary>
    /// Corrects the timing and music settings after load (see <see cref="Normalize"/> and <see cref="ShowAudio.Normalize"/>);
    /// a non-finite beat offset becomes 0.
    /// </summary>
    public void NormalizeAudio()
    {
        Normalize();
        if (!double.IsFinite(BeatOffsetSeconds))
        {
            BeatOffsetSeconds = 0;
        }

        Audio?.Normalize();
    }

    /// <summary>
    /// Corrects the show's tempo and length from a hand-edited or damaged file: a non-finite or non-positive BPM becomes
    /// <see cref="DefaultBpm"/>, any other BPM is clamped to <see cref="MinBpm"/>..<see cref="MaxBpm"/> (like the editor);
    /// a non-finite or non-positive length becomes <see cref="DefaultLengthSeconds"/>, a longer one than
    /// <see cref="MaxLengthSeconds"/> is cut to it.
    /// </summary>
    public void Normalize()
    {
        Bpm = double.IsFinite(Bpm) && Bpm > 0 ? Math.Clamp(Bpm, MinBpm, MaxBpm) : DefaultBpm;
        LengthSeconds = double.IsFinite(LengthSeconds) && LengthSeconds > 0
            ? Math.Min(LengthSeconds, MaxLengthSeconds)
            : DefaultLengthSeconds;
    }

    /// <summary>All items of all tracks.</summary>
    [JsonIgnore]
    public IEnumerable<ShowItem> AllItems => Tracks.SelectMany(t => t.Items);

    public ShowTrack? FindTrack(Guid trackId) => Tracks.FirstOrDefault(t => t.Id == trackId);

    /// <summary>The track holding <paramref name="itemId"/>, or null.</summary>
    public ShowTrack? FindTrackOfItem(Guid itemId) => Tracks.FirstOrDefault(t => t.FindItem(itemId) is not null);
}
