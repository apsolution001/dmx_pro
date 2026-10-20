namespace DmxPro.Audio.Analysis;

/// <summary>
/// What the app knows about a song after analysis: length, a downsampled waveform for drawing, and the beat grid.
/// </summary>
/// <param name="DurationSeconds">Song length.</param>
/// <param name="PeaksPerSecond">How many entries of <paramref name="Peaks"/> cover one second.</param>
/// <param name="Peaks">Waveform envelope: peak absolute amplitude (mono, 0..1) per bucket.</param>
/// <param name="Bpm">Detected tempo, or null when the song has no steady beat.</param>
/// <param name="FirstBeatSeconds">Time of the first beat of the grid (0 when <paramref name="Bpm"/> is null).</param>
public sealed record AudioAnalysis(double DurationSeconds, int PeaksPerSecond, float[] Peaks, double? Bpm, double FirstBeatSeconds);
