using DmxPro.Audio.Analysis;
using NAudio.Wave;

namespace DmxPro.Audio.Tests.Analysis;

public sealed class AudioAnalyzerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private string CacheDirectory => Path.Combine(_folder.Path, "cache");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AnalyzeAsync_StereoClickTrackWav_ReturnsDurationPeaksAndTempo()
    {
        var path = WriteWav("click.wav", TestSignals.ClickTrack(120, 20, 44100, introSeconds: 0.5), 44100, channels: 2);
        var analyzer = new AudioAnalyzer(CacheDirectory);

        var result = await analyzer.AnalyzeAsync(path);

        Assert.Equal(20.0, result.DurationSeconds, 0.01);
        Assert.Equal(AudioAnalyzer.PeaksPerSecond, result.PeaksPerSecond);
        Assert.Equal(2000, result.Peaks.Length);
        Assert.NotNull(result.Bpm);
        Assert.InRange(result.Bpm.Value, 119.5, 120.5);
        Assert.InRange(result.FirstBeatSeconds, 0.48, 0.52);
        Assert.All(result.Peaks.AsSpan(0, 45).ToArray(), peak => Assert.True(peak < 0.01f));
        Assert.InRange(result.Peaks.Max(), 0.5f, 1f);
        Assert.Equal(1, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_Mono22050Wav_PeaksCoverDuration()
    {
        var path = WriteWav("mono.wav", TestSignals.ClickTrack(140, 12.345, 22050), 22050, channels: 1);

        var result = await new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path);

        Assert.Equal(12.345, result.DurationSeconds, 0.001);
        Assert.Equal((int)Math.Ceiling(result.DurationSeconds * 100), result.Peaks.Length);
        Assert.NotNull(result.Bpm);
        Assert.InRange(result.Bpm.Value, 139.5, 140.5);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalysisRateWav_NoResamplingNeeded()
    {
        var path = WriteWav("lowrate.wav", TestSignals.ClickTrack(128, 15, AudioAnalyzer.AnalysisSampleRate), AudioAnalyzer.AnalysisSampleRate, channels: 1);

        var result = await new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path);

        Assert.NotNull(result.Bpm);
        Assert.InRange(result.Bpm.Value, 127.5, 128.5);
        Assert.InRange(result.FirstBeatSeconds, 0, 0.02);
    }

    [Fact]
    public async Task AnalyzeAsync_SilentWav_NoTempoZeroPeaks()
    {
        var path = WriteWav("silence.wav", new float[44100 * 6], 44100, channels: 2);

        var result = await new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path);

        Assert.Null(result.Bpm);
        Assert.Equal(0, result.FirstBeatSeconds);
        Assert.Equal(600, result.Peaks.Length);
        Assert.All(result.Peaks, peak => Assert.Equal(0f, peak));
    }

    [Fact]
    public async Task AnalyzeAsync_SecondCallSameInstance_ReturnsMemoryCachedResult()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 8, 22050), 22050, channels: 1);
        var analyzer = new AudioAnalyzer(CacheDirectory);

        var first = await analyzer.AnalyzeAsync(path);
        var second = await analyzer.AnalyzeAsync(path);

        Assert.Same(first, second);
        Assert.Equal(1, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_NewInstance_ReadsDiskCacheWithoutDecoding()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 8, 22050, noiseAmplitude: 0.05f), 22050, channels: 1);
        var original = await new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path);
        var analyzer = new AudioAnalyzer(CacheDirectory);
        var progress = new RecordingProgress();

        var cached = await analyzer.AnalyzeAsync(path, progress);

        Assert.Equal(0, analyzer.DecodeCount);
        AssertSameAnalysis(original, cached);
        Assert.Equal(1.0, progress.Values[^1]);
        Assert.True(File.Exists(analyzer.GetCacheFilePath(path)));
    }

    [Fact]
    public async Task AnalyzeAsync_CorruptCacheFile_IsRebuilt()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(100, 8, 22050), 22050, channels: 1);
        var original = await new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path);
        var analyzer = new AudioAnalyzer(CacheDirectory);
        var cacheFile = analyzer.GetCacheFilePath(path);
        var bytes = File.ReadAllBytes(cacheFile);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(cacheFile, bytes);

        var rebuilt = await analyzer.AnalyzeAsync(path);

        Assert.Equal(1, analyzer.DecodeCount);
        AssertSameAnalysis(original, rebuilt);
        var reread = new AudioAnalyzer(CacheDirectory);
        AssertSameAnalysis(original, await reread.AnalyzeAsync(path));
        Assert.Equal(0, reread.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_GarbageCacheFile_IsRebuilt()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(100, 6, 22050), 22050, channels: 1);
        var analyzer = new AudioAnalyzer(CacheDirectory);
        Directory.CreateDirectory(CacheDirectory);
        File.WriteAllText(analyzer.GetCacheFilePath(path), "garbage");

        var result = await analyzer.AnalyzeAsync(path);

        Assert.Equal(1, analyzer.DecodeCount);
        Assert.Equal(600, result.Peaks.Length);
    }

    [Fact]
    public async Task AnalyzeAsync_FileChanged_AnalysesAgain()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 6, 22050), 22050, channels: 1);
        var analyzer = new AudioAnalyzer(CacheDirectory);
        await analyzer.AnalyzeAsync(path);

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        await analyzer.AnalyzeAsync(path);

        Assert.Equal(2, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_ConcurrentCallsSameFile_DecodeOnce()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 30, 44100), 44100, channels: 2);
        var analyzer = new AudioAnalyzer(CacheDirectory);

        var results = await Task.WhenAll(analyzer.AnalyzeAsync(path), analyzer.AnalyzeAsync(path), analyzer.AnalyzeAsync(path));

        Assert.Equal(1, analyzer.DecodeCount);
        Assert.Same(results[0], results[1]);
        Assert.Same(results[0], results[2]);
    }

    [Fact]
    public async Task AnalyzeAsync_MoreFilesThanMemoryCapacity_OldestEvicted()
    {
        var analyzer = new AudioAnalyzer(CacheDirectory);
        var paths = new List<string>();
        for (var i = 0; i <= AudioAnalyzer.MemoryCacheCapacity; i++)
        {
            paths.Add(WriteWav($"f{i}.wav", TestSignals.Sine(220 + i, 1, 8000), 8000, channels: 1));
            await analyzer.AnalyzeAsync(paths[^1]);
        }

        Directory.Delete(CacheDirectory, recursive: true);
        var decodes = analyzer.DecodeCount;
        await analyzer.AnalyzeAsync(paths[^1]);
        Assert.Equal(decodes, analyzer.DecodeCount);
        await analyzer.AnalyzeAsync(paths[0]);
        Assert.Equal(decodes + 1, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_Progress_RisesToOne()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 20, 44100), 44100, channels: 2);
        var progress = new RecordingProgress();

        await new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path, progress);

        var values = progress.Values;
        Assert.True(values.Count >= 10, $"only {values.Count} progress reports");
        Assert.Equal(0.0, values[0]);
        Assert.Equal(1.0, values[^1]);
        for (var i = 1; i < values.Count; i++)
        {
            Assert.True(values[i] >= values[i - 1], $"progress went back at {i}");
        }
    }

    [Fact]
    public async Task AnalyzeAsync_AlreadyCancelled_Throws()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 5, 22050), 22050, channels: 1);
        var analyzer = new AudioAnalyzer(CacheDirectory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(path, cancellationToken: new CancellationToken(true)));
        Assert.Equal(0, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_CancelledMidway_StopsWritesNoCacheAndCanRunAgain()
    {
        var path = WriteWav("long.wav", TestSignals.ClickTrack(128, 60, 44100), 44100, channels: 2);
        var analyzer = new AudioAnalyzer(CacheDirectory);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(value =>
        {
            if (value > 0.05)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(path, progress, cancellation.Token));

        // The abandoned job stops at its next chunk; it must end without completing or writing a cache file.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (analyzer.ActiveJobCount > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, analyzer.ActiveJobCount);
        Assert.False(File.Exists(analyzer.GetCacheFilePath(path)));

        var result = await analyzer.AnalyzeAsync(path);
        Assert.NotNull(result.Bpm);
        Assert.Equal(2, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_OneOfTwoCallersCancels_OtherStillGetsResult()
    {
        var path = WriteWav("a.wav", TestSignals.ClickTrack(128, 30, 44100), 44100, channels: 2);
        var analyzer = new AudioAnalyzer(CacheDirectory);
        using var cancellation = new CancellationTokenSource();
        var keptRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var kept = analyzer.AnalyzeAsync(path, new RecordingProgress(v => keptRunning.TrySetResult()));
        var cancelled = analyzer.AnalyzeAsync(path, new RecordingProgress(v => cancelledRunning.TrySetResult()), cancellation.Token);
        await Task.WhenAll(keptRunning.Task, cancelledRunning.Task).WaitAsync(TimeSpan.FromSeconds(30));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var result = await kept;
        Assert.NotNull(result.Bpm);
        Assert.Equal(1, analyzer.DecodeCount);
    }

    [Fact]
    public async Task AnalyzeAsync_MissingFile_ThrowsAudioException()
    {
        var analyzer = new AudioAnalyzer(CacheDirectory);

        var error = await Assert.ThrowsAsync<AudioException>(() => analyzer.AnalyzeAsync(Path.Combine(_folder.Path, "nope.mp3")));
        Assert.Contains("nope.mp3", error.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_UnsupportedExtension_ThrowsAudioException()
    {
        var path = Path.Combine(_folder.Path, "notes.txt");
        File.WriteAllText(path, "hello");

        await Assert.ThrowsAsync<AudioException>(() => new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path));
    }

    [Fact]
    public async Task AnalyzeAsync_CorruptWav_ThrowsAudioException()
    {
        var path = Path.Combine(_folder.Path, "broken.wav");
        File.WriteAllBytes(path, [.. "RIFF"u8, 1, 2, 3, 4, .. "garbage that is not a wave file"u8]);

        var error = await Assert.ThrowsAsync<AudioException>(() => new AudioAnalyzer(CacheDirectory).AnalyzeAsync(path));
        Assert.Contains("broken.wav", error.Message);
    }

    [Fact]
    public void Constructor_EmptyCacheDirectory_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AudioAnalyzer(" "));
    }

    private static void AssertSameAnalysis(AudioAnalysis expected, AudioAnalysis actual)
    {
        Assert.Equal(expected.DurationSeconds, actual.DurationSeconds);
        Assert.Equal(expected.PeaksPerSecond, actual.PeaksPerSecond);
        Assert.Equal(expected.Peaks, actual.Peaks);
        Assert.Equal(expected.Bpm, actual.Bpm);
        Assert.Equal(expected.FirstBeatSeconds, actual.FirstBeatSeconds);
    }

    private string WriteWav(string name, float[] mono, int sampleRate, int channels)
    {
        var path = Path.Combine(_folder.Path, name);
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        var frame = new float[channels];
        foreach (var sample in mono)
        {
            Array.Fill(frame, sample);
            writer.WriteSamples(frame, 0, channels);
        }

        return path;
    }

    /// <summary>Synchronous progress sink (Progress&lt;T&gt; would post to the thread pool and reorder values).</summary>
    private sealed class RecordingProgress(Action<double>? onReport = null) : IProgress<double>
    {
        private readonly List<double> _values = [];

        public List<double> Values
        {
            get
            {
                lock (_values)
                {
                    return [.. _values];
                }
            }
        }

        public void Report(double value)
        {
            lock (_values)
            {
                _values.Add(value);
            }

            onReport?.Invoke(value);
        }
    }
}
