using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmxPro.Audio.Playback;

/// <summary>
/// <see cref="IAudioPlayer"/> on Windows: Media Foundation decoding (MP3, WAV, M4A/AAC, FLAC) to a shared-mode output
/// device, with smooth volume and a sample-accurate fade-out.
/// <para>
/// <b>Chain:</b> Media Foundation decode (<see cref="SampleDecoderFactory"/>: the whole song into memory for frame-exact
/// seeks, or streamed for very long files) → <see cref="PlaybackSource"/> (volume ramp, click-free transition
/// ramps, fade-out window, heard-position map) → <see cref="DeviceAudioOutput"/> (WASAPI shared, event sync, 80 ms;
/// WaveOut fallback).
/// </para>
/// <para>
/// <b>Position (the master clock):</b> the device reports how many frames it has really played; the source remembers
/// which song frame each output frame carried, so <see cref="Position"/> is the song frame being heard now — buffering,
/// latency and resampling cancel out, accurate to the device clock (~1 ms on WASAPI). After a seek / play it holds at
/// the target until the new audio is actually heard, then advances; while playing it never goes backwards. Paused or
/// stopped it is the stored position.
/// </para>
/// <para>
/// <b>Pause / seek:</b> never stop the device abruptly. The source fades the audio out over 8 ms and plays silence (or
/// fades in at the new position); a monitor timer stops the device once the silence is heard, so an idle player holds
/// no running audio stream. Resume is a seek to the paused position, so audio and <see cref="Position"/> always agree.
/// </para>
/// <para>
/// <b>Threads:</b> public members may be called from any thread (UI, engine). <c>_transportLock</c> serialises transport
/// operations; <c>_stateLock</c> guards the state read by the getters and is held only briefly (never across a device
/// Stop), so the engine reading <see cref="Position"/> is never blocked behind device work. The audio thread only takes
/// the source's own locks. <see cref="Error"/> is raised on the thread pool, never while a lock is held, so a handler
/// may call any member, including <see cref="Dispose"/>.
/// </para>
/// <para>Loading a song clears any fade-out set with <see cref="SetFadeOut"/>; the volume is kept.</para>
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    /// <summary>How often the monitor checks for "silence heard → stop the device" and the end of the song.</summary>
    internal static readonly TimeSpan DefaultMonitorInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Extra silence that must be heard before the device is stopped (device clock interpolation margin).</summary>
    private const double StopMarginSeconds = 0.020;

    /// <summary>Bound for frame numbers from arbitrary times (far beyond any song), so frame arithmetic never overflows.</summary>
    private const double MaxFrameMagnitude = 1L << 52;

    /// <summary>Longest wait for a playing song to fade out when another song is loaded.</summary>
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromMilliseconds(300);

    private readonly IAudioOutputFactory _outputFactory;
    private readonly Func<string, CancellationToken, ISampleDecoder> _decoderFactory;
    private readonly ILogger _logger;
    private readonly TimeSpan _monitorInterval;
    private readonly Timer? _monitor;
    private readonly object _transportLock = new();
    private readonly object _stateLock = new();

    // Guarded by _stateLock (written only while also holding _transportLock).
    private Session? _session;
    private bool _playing;
    private long _positionFrame;
    private int _generation;
    private float _volume = 1f;
    private bool _disposed;
    private int _loadVersion;
    private EventHandler<AudioErrorEventArgs>? _error;

    public AudioPlayer(ILogger<AudioPlayer>? logger = null)
        : this(new DeviceAudioOutputFactory(), SampleDecoderFactory.Open, logger, DefaultMonitorInterval)
    {
    }

    /// <summary>
    /// Test seam: inject the output device and decoder. A <paramref name="monitorInterval"/> of
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables the timer (tests call <see cref="Poll"/>).
    /// </summary>
    internal AudioPlayer(
        IAudioOutputFactory outputFactory,
        Func<string, CancellationToken, ISampleDecoder> decoderFactory,
        ILogger? logger,
        TimeSpan monitorInterval)
    {
        _outputFactory = outputFactory;
        _decoderFactory = decoderFactory;
        _logger = logger ?? NullLogger.Instance;
        _monitorInterval = monitorInterval;
        if (monitorInterval != Timeout.InfiniteTimeSpan)
        {
            _monitor = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    public event EventHandler<AudioErrorEventArgs>? Error
    {
        add
        {
            lock (_stateLock)
            {
                _error += value;
            }
        }

        remove
        {
            lock (_stateLock)
            {
                _error -= value;
            }
        }
    }

    public string? FilePath
    {
        get
        {
            lock (_stateLock)
            {
                return _session?.Path;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_stateLock)
            {
                return _session is { } s ? FramesToTime(s.Source.LengthFrames, s.SampleRate) : TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_stateLock)
            {
                if (_session is not { } s)
                {
                    return TimeSpan.Zero;
                }

                RefreshLocked(s);
                long length = s.Source.LengthFrames;
                return FramesToTime(length > 0 ? Math.Min(_positionFrame, length) : _positionFrame, s.SampleRate);
            }
        }
    }

    public long MemoryBytes
    {
        get
        {
            lock (_stateLock)
            {
                return _session?.MemoryBytes ?? 0;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_stateLock)
            {
                if (_session is { } s)
                {
                    RefreshLocked(s);
                }

                return _playing;
            }
        }
    }

    public float Volume
    {
        get
        {
            lock (_stateLock)
            {
                return _volume;
            }
        }

        set
        {
            float volume = PlaybackSampleProcessor.ClampVolume(value);
            lock (_stateLock)
            {
                _volume = volume;
                _session?.Source.SetVolume(volume);
            }
        }
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!AudioFormats.IsSupported(path))
        {
            throw new AudioException(
                $"This file cannot be played: \"{System.IO.Path.GetFileName(path)}\" is not a supported type (MP3, WAV, M4A/AAC or FLAC).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Session? old;
        int version;
        lock (_transportLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            version = ++_loadVersion;
            lock (_stateLock)
            {
                old = _session;
                _session = null;
                _playing = false;
                _positionFrame = 0;
            }

            StopMonitor();
        }

        // Device and file work stays off the caller's (UI) thread. The thread pool is MTA, which the Media Foundation
        // objects need so the (MTA) audio thread can use them directly.
        Session session = await Task.Run(
            () =>
            {
                if (old is not null)
                {
                    CloseSession(old, graceful: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return OpenSession(path, cancellationToken);
            },
            CancellationToken.None).ConfigureAwait(false);

        bool attached = false;
        lock (_transportLock)
        {
            if (!_disposed && version == _loadVersion && !cancellationToken.IsCancellationRequested)
            {
                lock (_stateLock)
                {
                    _session = session;
                    _playing = false;
                    _positionFrame = 0;
                    session.Source.SetVolume(_volume);
                }

                attached = true;
            }
        }

        if (!attached)
        {
            CloseSession(session, graceful: false);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            throw new OperationCanceledException("A newer song was loaded while this one was opening.");
        }

        _logger.LogInformation("Loaded {File} ({Duration})", path, Duration);
    }

    public void Play()
    {
        lock (_transportLock)
        {
            if (_disposed || _session is not { } s)
            {
                return;
            }

            long from;
            int generation;
            lock (_stateLock)
            {
                RefreshLocked(s);
                long length = s.Source.LengthFrames;
                if (_playing || (length > 0 && _positionFrame >= length))
                {
                    return; // already playing, or at the end: nothing to hear
                }

                from = _positionFrame;
                generation = ++_generation;
            }

            if (s.Output is null && !TryRecreateOutput(s))
            {
                return;
            }

            s.Source.Run(from, generation);
            if (!s.DeviceRunning)
            {
                s.Source.ResetOutputCounter();
                try
                {
                    s.Output!.Play();
                }
                catch (Exception ex)
                {
                    s.Source.Halt();
                    _logger.LogWarning(ex, "Audio device failed to start");
                    RaiseError(ex is AudioException ? ex.Message : $"The audio device could not start: {ex.Message}", ex);
                    return;
                }
            }

            lock (_stateLock)
            {
                s.DeviceRunning = true;
                _playing = true;
            }

            StartMonitor();
        }
    }

    public void Pause()
    {
        lock (_transportLock)
        {
            if (_disposed || _session is not { } s)
            {
                return;
            }

            lock (_stateLock)
            {
                RefreshLocked(s);
                if (!_playing)
                {
                    return;
                }

                _playing = false; // Position freezes at what is heard now; resume seeks back to it
            }

            s.Source.Halt();
        }
    }

    public void Stop()
    {
        lock (_transportLock)
        {
            if (_disposed || _session is not { } s)
            {
                return;
            }

            lock (_stateLock)
            {
                _playing = false;
                _positionFrame = 0;
            }

            s.Source.Halt();
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_transportLock)
        {
            if (_disposed || _session is not { } s)
            {
                return;
            }

            long length = s.Source.LengthFrames;
            long frame = TimeToFrames(position, s.SampleRate);
            if (length > 0)
            {
                frame = Math.Min(frame, length);
            }

            int generation = 0;
            lock (_stateLock)
            {
                RefreshLocked(s);
                _positionFrame = frame;
                if (_playing)
                {
                    generation = ++_generation;
                }
            }

            if (generation != 0)
            {
                s.Source.Run(frame, generation);
            }
        }
    }

    public void SetFadeOut(TimeSpan start, TimeSpan duration)
    {
        lock (_transportLock)
        {
            if (_disposed || _session is not { } s)
            {
                return;
            }

            if (duration <= TimeSpan.Zero)
            {
                s.Source.SetFadeOut(0, 0);
                return;
            }

            // Signed: a start before the song means the fade is already partly done at song time 0 (the window is cut,
            // not moved), so the gain at every song time is the same straight line from start to start + duration.
            long startFrame = TimeToSignedFrames(start, s.SampleRate);
            long lengthFrames = Math.Max(1, TimeToFrames(duration, s.SampleRate));
            s.Source.SetFadeOut(startFrame, lengthFrames);
        }
    }

    public void Dispose()
    {
        Session? session;
        lock (_transportLock)
        {
            if (_disposed)
            {
                return;
            }

            lock (_stateLock)
            {
                _disposed = true;
                session = _session;
                _session = null;
                _playing = false;
                _positionFrame = 0;
                _error = null;
            }

            _monitor?.Dispose();
        }

        if (session is not null)
        {
            CloseSession(session, graceful: false);
        }
    }

    /// <summary>
    /// Monitor step (timer, or tests): detects the end of the song and stops the device once its trailing silence has
    /// been heard.
    /// </summary>
    internal void Poll()
    {
        lock (_transportLock)
        {
            if (_disposed || _session is not { } s)
            {
                return;
            }

            IAudioOutput? stop = null;
            lock (_stateLock)
            {
                RefreshLocked(s);
                if (!_playing && s.DeviceRunning && s.Output is { } output)
                {
                    long silentSince = s.Source.SilentSinceOutputFrame;
                    long margin = (long)(s.SampleRate * StopMarginSeconds);
                    if (silentSince >= 0 && output.PlayedFrames >= silentSince + margin)
                    {
                        s.DeviceRunning = false;
                        stop = output;
                    }
                }
            }

            if (stop is not null)
            {
                stop.Stop(); // joins the device thread; the getters no longer touch the device
            }

            if (!s.DeviceRunning)
            {
                StopMonitor();
            }
        }
    }

    internal static long TimeToFrames(TimeSpan time, int sampleRate) =>
        time <= TimeSpan.Zero ? 0 : (long)Math.Round(time.Ticks * (double)sampleRate / TimeSpan.TicksPerSecond);

    /// <summary>Like <see cref="TimeToFrames"/> but keeps negative times (song positions before the song start).</summary>
    internal static long TimeToSignedFrames(TimeSpan time, int sampleRate) =>
        (long)Math.Clamp(Math.Round(time.Ticks * (double)sampleRate / TimeSpan.TicksPerSecond), -MaxFrameMagnitude, MaxFrameMagnitude);

    internal static TimeSpan FramesToTime(long frames, int sampleRate) =>
        frames <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)Math.Round(frames * (double)TimeSpan.TicksPerSecond / sampleRate));

    /// <summary>Folds what the device is playing now into the state: position, and the end of the song.</summary>
    private void RefreshLocked(Session s)
    {
        if (!_playing || !s.DeviceRunning || s.Output is not { } output)
        {
            return;
        }

        if (!s.Source.TryMapOutputFrame(output.PlayedFrames, out HeardFrame heard) || heard.Generation != _generation)
        {
            return; // still hearing audio from before the last seek / play: hold at its target
        }

        if (heard.Kind == OutputSpanKind.End)
        {
            _playing = false;
            _positionFrame = Math.Max(heard.SourceFrame, s.Source.LengthFrames);
            return;
        }

        if (heard.SourceFrame > _positionFrame)
        {
            _positionFrame = heard.SourceFrame;
        }
    }

    private Session OpenSession(string path, CancellationToken cancellationToken)
    {
        ISampleDecoder decoder;
        try
        {
            decoder = _decoderFactory(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not AudioException and not OperationCanceledException)
        {
            throw new AudioException($"This file cannot be played: {ex.Message}", ex);
        }

        var source = new PlaybackSource(decoder);
        var session = new Session(path, source, decoder is MemorySampleDecoder memory ? memory.MemoryBytes : 0);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.DecodeFailed = ex => OnDecodeFailed(session, ex);
            session.Output = CreateOutput(session);
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }
        catch
        {
            CloseSession(session, graceful: false);
            throw;
        }
    }

    private IAudioOutput CreateOutput(Session session)
    {
        IAudioOutput output;
        try
        {
            output = _outputFactory.Create();
        }
        catch (Exception ex) when (ex is not AudioException)
        {
            throw new AudioException($"No audio output device is available: {ex.Message}", ex);
        }

        try
        {
            output.Init(session.Source);
        }
        catch (Exception ex)
        {
            output.Dispose();
            throw ex is AudioException ? ex : new AudioException($"No audio output device is available: {ex.Message}", ex);
        }

        output.Failed += (_, ex) => OnOutputFailed(session, output, ex);
        return output;
    }

    private bool TryRecreateOutput(Session s)
    {
        try
        {
            IAudioOutput output = CreateOutput(s);
            lock (_stateLock)
            {
                s.Output = output;
                s.DeviceRunning = false;
            }

            return true;
        }
        catch (AudioException ex)
        {
            _logger.LogWarning(ex, "No audio output device");
            RaiseError(ex.Message, ex);
            return false;
        }
    }

    private void CloseSession(Session session, bool graceful)
    {
        IAudioOutput? output;
        bool running;
        lock (_stateLock)
        {
            output = session.Output;
            running = session.DeviceRunning;
            session.Output = null;
            session.DeviceRunning = false;
        }

        if (output is not null)
        {
            if (graceful && running)
            {
                FadeOutBeforeClose(session.Source, output);
            }

            try
            {
                output.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Releasing the audio device failed");
            }
        }

        session.Source.Dispose();
    }

    /// <summary>Lets a playing song fade out (no click) before its device is released. Background threads only.</summary>
    private static void FadeOutBeforeClose(PlaybackSource source, IAudioOutput output)
    {
        source.Halt();
        long margin = (long)(source.SampleRate * StopMarginSeconds);
        long deadline = Environment.TickCount64 + (long)GracefulCloseTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            long silentSince = source.SilentSinceOutputFrame;
            if (silentSince >= 0 && output.PlayedFrames >= silentSince + margin)
            {
                return;
            }

            Thread.Sleep(5);
        }
    }

    private void OnOutputFailed(Session session, IAudioOutput output, Exception? exception) =>
        ThreadPool.QueueUserWorkItem(_ => HandleOutputFailed(session, output, exception));

    private void HandleOutputFailed(Session session, IAudioOutput output, Exception? exception)
    {
        lock (_transportLock)
        {
            if (_disposed || !ReferenceEquals(_session, session) || !ReferenceEquals(session.Output, output))
            {
                return;
            }

            lock (_stateLock)
            {
                RefreshLocked(session);
                _playing = false;
                session.Output = null;
                session.DeviceRunning = false;
            }

            session.Source.SilenceImmediately();
            try
            {
                output.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Releasing the failed audio device failed");
            }

            StopMonitor();
        }

        _logger.LogWarning(exception, "Audio device stopped unexpectedly");
        RaiseError(
            exception is null
                ? "The audio device stopped (it may have been unplugged). Playback has stopped."
                : $"The audio device stopped: {exception.Message} Playback has stopped.",
            exception);
    }

    private void OnDecodeFailed(Session session, Exception exception) =>
        ThreadPool.QueueUserWorkItem(_ => HandleDecodeFailed(session, exception));

    private void HandleDecodeFailed(Session session, Exception exception)
    {
        lock (_transportLock)
        {
            if (_disposed || !ReferenceEquals(_session, session))
            {
                return;
            }

            lock (_stateLock)
            {
                RefreshLocked(session);
                _playing = false;
            }

            session.Source.Halt();
        }

        _logger.LogWarning(exception, "Decoding {File} failed", session.Path);
        RaiseError(
            exception is AudioException
                ? exception.Message
                : $"This file cannot be played: \"{System.IO.Path.GetFileName(session.Path)}\" could not be decoded ({exception.Message}).",
            exception);
    }

    private void RaiseError(string message, Exception? exception)
    {
        EventHandler<AudioErrorEventArgs>? handler;
        lock (_stateLock)
        {
            handler = _error;
        }

        if (handler is null)
        {
            return;
        }

        var args = new AudioErrorEventArgs(message, exception);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An audio Error event handler threw");
            }
        });
    }

    private void StartMonitor() => _monitor?.Change(_monitorInterval, _monitorInterval);

    private void StopMonitor() => _monitor?.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>One loaded song: its source and (while a device is available) its output.</summary>
    private sealed class Session(string path, PlaybackSource source, long memoryBytes)
    {
        public string Path { get; } = path;

        /// <summary>Bytes held by the decoded song (fixed once decoded); 0 when streamed.</summary>
        public long MemoryBytes { get; } = memoryBytes;

        public PlaybackSource Source { get; } = source;

        public int SampleRate => Source.SampleRate;

        /// <summary>Null after the device failed; <see cref="Play"/> creates a new one.</summary>
        public IAudioOutput? Output { get; set; }

        /// <summary>True while the device pulls samples (playing, or playing out a fade before it is stopped).</summary>
        public bool DeviceRunning { get; set; }
    }
}
