namespace DmxPro.Audio.Analysis;

/// <summary>
/// Analyses music files off the UI thread (decode, waveform peaks, tempo/beat grid), caching results on disk per file
/// (path + size + last write time) so a song is analysed once.
/// </summary>
public interface IAudioAnalyzer
{
    /// <summary>
    /// The analysis of a file (from the cache when the file is unchanged). <paramref name="progress"/> reports 0..1.
    /// Throws <see cref="AudioException"/> when the file cannot be decoded; <see cref="OperationCanceledException"/> on cancel.
    /// </summary>
    Task<AudioAnalysis> AnalyzeAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
