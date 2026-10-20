namespace DmxPro.Core.Shows;

/// <summary>One line of the beat grid: its show time, beat index (see <see cref="TimelineSnap.BeatIndexAt"/>) and whether it starts a bar.</summary>
public readonly record struct BeatLine(double Seconds, long BeatIndex, bool IsBarStart);
