using System.Text.Json.Serialization;

namespace DmxPro.Core.Shows;

/// <summary>A timeline: tracks holding function items placed in time, and optionally one song (<see cref="Audio"/>).</summary>
public sealed class Show
{
    public const double DefaultLengthSeconds = 180;
    public const double DefaultBpm = 120;

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

    /// <summary>Corrects the music settings after load (see <see cref="ShowAudio.Normalize"/>); a non-finite beat offset becomes 0.</summary>
    public void NormalizeAudio()
    {
        if (!double.IsFinite(BeatOffsetSeconds))
        {
            BeatOffsetSeconds = 0;
        }

        Audio?.Normalize();
    }

    /// <summary>All items of all tracks.</summary>
    [JsonIgnore]
    public IEnumerable<ShowItem> AllItems => Tracks.SelectMany(t => t.Items);

    public ShowTrack? FindTrack(Guid trackId) => Tracks.FirstOrDefault(t => t.Id == trackId);

    /// <summary>The track holding <paramref name="itemId"/>, or null.</summary>
    public ShowTrack? FindTrackOfItem(Guid itemId) => Tracks.FirstOrDefault(t => t.FindItem(itemId) is not null);
}
