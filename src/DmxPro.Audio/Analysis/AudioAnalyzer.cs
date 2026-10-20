using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DmxPro.Audio.Analysis;

/// <summary>
/// <see cref="IAudioAnalyzer"/>: decodes with NAudio (WaveFileReader for WAV, MediaFoundationReader otherwise) to mono
/// float, streams it through <see cref="WaveformPeaks"/> (full rate) and an <see cref="OnsetEnvelope"/> (resampled to
/// <see cref="AnalysisSampleRate"/>), runs <see cref="BeatDetector"/>, and caches the result in
/// <see cref="CacheDirectory"/> (and the last few results in memory). The decoded song is never held in memory; peak use
/// is a few MB regardless of length. Concurrent requests for the same file share one analysis. Thread-safe.
/// </summary>
/// <remarks>The returned <see cref="AudioAnalysis.Peaks"/> array is shared between callers and must not be modified.</remarks>
public sealed class AudioAnalyzer : IAudioAnalyzer
{
    /// <summary>Waveform resolution stored in the cache.</summary>
    public const int PeaksPerSecond = 100;

    /// <summary>Bump when the analysis output changes; older cache files are then ignored and rebuilt.</summary>
    internal const int AnalysisVersion = 1;

    /// <summary>Sample rate the beat analysis runs at (enough for onsets, a quarter of the work of 44.1 kHz).</summary>
    internal const int AnalysisSampleRate = 11025;

    /// <summary>How many recent analyses are kept in memory.</summary>
    internal const int MemoryCacheCapacity = 8;

    private const int ReadChunkSamples = 8192;
    private const double DecodeProgressShare = 0.95;
    private const long MaxCacheFileBytes = 256L * 1024 * 1024;

    private readonly ILogger<AudioAnalyzer> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, AnalysisJob> _jobs = new(StringComparer.Ordinal);
    private readonly LinkedList<KeyValuePair<string, AudioAnalysis>> _recent = new();
    private int _decodeCount;

    public AudioAnalyzer(string cacheDirectory)
        : this(cacheDirectory, NullLogger<AudioAnalyzer>.Instance)
    {
    }

    public AudioAnalyzer(string cacheDirectory, ILogger<AudioAnalyzer> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        CacheDirectory = cacheDirectory;
        _logger = logger;
    }

    /// <summary>Folder for cache files (created on first write).</summary>
    public string CacheDirectory { get; }

    /// <summary>Number of times a file was actually decoded (test hook: cache hits do not decode).</summary>
    internal int DecodeCount => Volatile.Read(ref _decodeCount);

    /// <summary>Analyses currently running (test hook: a cancelled analysis must stop).</summary>
    internal int ActiveJobCount
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Count;
            }
        }
    }

    public async Task<AudioAnalysis> AnalyzeAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AudioFormats.IsSupported(path))
        {
            throw new AudioException($"\"{Path.GetFileName(path)}\" is not a supported music file (MP3, WAV, M4A/AAC or FLAC).");
        }

        // Even the file lookup goes off the calling thread: the path may be on a slow network share.
        var source = await Task.Run(() => AudioFileIdentity.Of(path), cancellationToken).ConfigureAwait(false);
        var key = AnalysisCacheFile.FileName(source);

        AnalysisJob job;
        lock (_gate)
        {
            if (TryGetRecent(key, out var recent))
            {
                progress?.Report(1);
                return recent;
            }

            if (!_jobs.TryGetValue(key, out job!) || job.IsAbandoned)
            {
                job = new AnalysisJob(_logger);
                _jobs[key] = job;
                var started = job;
                job.Start(() => Run(started, source, key));
                _ = job.Task.ContinueWith(task => OnJobFinished(started, key, task), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            job.Attach(progress);
        }

        try
        {
            return await job.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                job.Detach(progress);
            }
        }
    }

    /// <summary>Cache file that holds (or would hold) the analysis of <paramref name="path"/> in its current state.</summary>
    internal string GetCacheFilePath(string path) =>
        Path.Combine(CacheDirectory, AnalysisCacheFile.FileName(AudioFileIdentity.Of(path)));

    private AudioAnalysis Run(AnalysisJob job, AudioFileIdentity source, string key)
    {
        var token = job.Token;
        job.Report(0);
        var cachePath = Path.Combine(CacheDirectory, key);
        var cached = TryReadCache(cachePath, source);
        if (cached is not null)
        {
            job.Report(1);
            return cached;
        }

        token.ThrowIfCancellationRequested();
        var analysis = Decode(source, job, token);
        TryWriteCache(cachePath, analysis, source);
        job.Report(1);
        return analysis;
    }

    private void OnJobFinished(AnalysisJob job, string key, Task<AudioAnalysis> task)
    {
        lock (_gate)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                AddRecent(key, task.Result);
            }

            if (_jobs.TryGetValue(key, out var current) && ReferenceEquals(current, job))
            {
                _jobs.Remove(key);
            }
        }

        // Observe the exception so a failure nobody waited for (all callers cancelled) is not "unobserved".
        _ = task.Exception;
    }

    private AudioAnalysis Decode(AudioFileIdentity source, AnalysisJob job, CancellationToken token)
    {
        Interlocked.Increment(ref _decodeCount);
        var name = Path.GetFileName(source.FullPath);
        try
        {
            var (reader, samples) = OpenReader(source.FullPath);
            using (reader)
            {
                var duration = reader.TotalTime.TotalSeconds;
                var mono = new MonoDownmixSampleProvider(samples);
                var sourceRate = mono.WaveFormat.SampleRate;
                var expectedSeconds = double.IsFinite(duration) && duration > 0 ? duration : 0;
                var peaks = new WaveformPeakAccumulator(sourceRate, PeaksPerSecond, (int)Math.Min(int.MaxValue / 2, (expectedSeconds * PeaksPerSecond) + 2));
                ISampleProvider tap = new PeakTapSampleProvider(mono, peaks);
                var analysisStream = sourceRate == AnalysisSampleRate ? tap : new WdlResamplingSampleProvider(tap, AnalysisSampleRate);
                var onsets = new OnsetEnvelope(AnalysisSampleRate, expectedSeconds);

                var buffer = new float[ReadChunkSamples];
                var length = reader.Length;
                int read;
                while ((read = analysisStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    onsets.Add(buffer.AsSpan(0, read));
                    if (length > 0)
                    {
                        job.Report(Math.Clamp((double)reader.Position / length, 0, 1) * DecodeProgressShare);
                    }
                }

                token.ThrowIfCancellationRequested();
                if (!(double.IsFinite(duration) && duration > 0))
                {
                    duration = (double)peaks.SampleCount / sourceRate;
                }

                var (bpm, firstBeat) = BeatDetector.Detect(onsets, token);
                return new AudioAnalysis(duration, PeaksPerSecond, peaks.ToArray(), bpm, firstBeat);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AudioException)
        {
            _logger.LogWarning(ex, "Could not analyse {Path}", source.FullPath);
            throw new AudioException($"\"{name}\" could not be read. The file may be damaged or in an unsupported format.", ex);
        }
    }

    // WAV through the managed reader (exact, no codec needed); everything else, and WAV variants it cannot convert
    // (e.g. ADPCM), through Media Foundation. Both are created and read on the same worker (MTA) thread.
    private (WaveStream Reader, ISampleProvider Samples) OpenReader(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            WaveFileReader? wav = null;
            try
            {
                wav = new WaveFileReader(path);
                return (wav, wav.ToSampleProvider());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                wav?.Dispose();
                _logger.LogDebug(ex, "WAV reader cannot read {Path}; trying Media Foundation", path);
            }
        }

        var reader = new MediaFoundationReader(path, new MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true });
        try
        {
            return (reader, reader.ToSampleProvider());
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private AudioAnalysis? TryReadCache(string cachePath, AudioFileIdentity source)
    {
        try
        {
            var info = new FileInfo(cachePath);
            if (!info.Exists)
            {
                return null;
            }

            if (info.Length > MaxCacheFileBytes)
            {
                _logger.LogInformation("Ignoring analysis cache {CachePath}: file too large", cachePath);
                return null;
            }

            var analysis = AnalysisCacheFile.Deserialize(File.ReadAllBytes(cachePath), source, out var reason);
            if (analysis is null)
            {
                _logger.LogInformation("Ignoring analysis cache {CachePath}: {Reason}", cachePath, reason);
            }

            return analysis;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Analysis cache {CachePath} could not be read: {Error}", cachePath, ex.Message);
            return null;
        }
    }

    private void TryWriteCache(string cachePath, AudioAnalysis analysis, AudioFileIdentity source)
    {
        try
        {
            AnalysisCacheFile.WriteAtomic(cachePath, AnalysisCacheFile.Serialize(analysis, source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The analysis itself succeeded; it will simply be recomputed next time.
            _logger.LogWarning("Analysis cache {CachePath} could not be written: {Error}", cachePath, ex.Message);
        }
    }

    // Callers hold _gate.
    private bool TryGetRecent(string key, out AudioAnalysis analysis)
    {
        for (var node = _recent.First; node is not null; node = node.Next)
        {
            if (string.Equals(node.Value.Key, key, StringComparison.Ordinal))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                analysis = node.Value.Value;
                return true;
            }
        }

        analysis = null!;
        return false;
    }

    // Callers hold _gate.
    private void AddRecent(string key, AudioAnalysis analysis)
    {
        for (var node = _recent.First; node is not null; node = node.Next)
        {
            if (string.Equals(node.Value.Key, key, StringComparison.Ordinal))
            {
                _recent.Remove(node);
                break;
            }
        }

        _recent.AddFirst(new KeyValuePair<string, AudioAnalysis>(key, analysis));
        while (_recent.Count > MemoryCacheCapacity)
        {
            _recent.RemoveLast();
        }
    }

    /// <summary>
    /// One running analysis shared by every caller that asked for the same file. It is cancelled only when all of its
    /// callers have cancelled; progress goes to every attached caller.
    /// </summary>
    private sealed class AnalysisJob(ILogger logger)
    {
        private const double ProgressStep = 0.01;

        private readonly CancellationTokenSource _cancellation = new();
        private IProgress<double>[] _listeners = [];
        private int _waiters;
        private double _lastReported = double.NegativeInfinity;

        public Task<AudioAnalysis> Task { get; private set; } = null!;

        public CancellationToken Token => _cancellation.Token;

        /// <summary>True once every caller left; a new caller must start a fresh job.</summary>
        public bool IsAbandoned => _cancellation.IsCancellationRequested;

        public void Start(Func<AudioAnalysis> work) => Task = System.Threading.Tasks.Task.Run(work, _cancellation.Token);

        // Attach/Detach run under the analyzer's lock; Report runs on the worker and reads a copy-on-write array.
        public void Attach(IProgress<double>? progress)
        {
            _waiters++;
            if (progress is not null)
            {
                Volatile.Write(ref _listeners, [.. _listeners, progress]);
            }
        }

        public void Detach(IProgress<double>? progress)
        {
            _waiters--;
            if (progress is not null)
            {
                var index = Array.IndexOf(_listeners, progress);
                if (index >= 0)
                {
                    var copy = new List<IProgress<double>>(_listeners);
                    copy.RemoveAt(index);
                    Volatile.Write(ref _listeners, copy.ToArray());
                }
            }

            if (_waiters == 0 && !Task.IsCompleted)
            {
                _cancellation.Cancel();
            }
        }

        public void Report(double value)
        {
            // Throttled to 1 % steps so a UI Progress<T> is not flooded; 0 and 1 always go through.
            if (value > 0 && value < 1 && value < _lastReported + ProgressStep)
            {
                return;
            }

            _lastReported = value;
            var listeners = Volatile.Read(ref _listeners);
            for (var i = 0; i < listeners.Length; i++)
            {
                try
                {
                    listeners[i].Report(value);
                }
                catch (Exception ex)
                {
                    // One caller's broken progress handler must not fail the analysis shared with other callers.
                    logger.LogWarning(ex, "Analysis progress handler failed");
                }
            }
        }
    }
}
