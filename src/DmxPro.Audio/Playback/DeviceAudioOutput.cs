using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DmxPro.Audio.Playback;

/// <summary>
/// <see cref="IAudioOutput"/> on the default Windows sound device: WASAPI shared mode with event sync and
/// <see cref="WasapiLatencyMilliseconds"/> of buffering (the mixer converts rate/format itself), falling back to
/// WinMM <see cref="WaveOutEvent"/> when WASAPI cannot start. The played position comes from the device clock
/// (WASAPI IAudioClock, QPC-interpolated; waveOutGetPosition for the fallback).
/// </summary>
internal sealed class DeviceAudioOutput : IAudioOutput
{
    public const int WasapiLatencyMilliseconds = 80;
    public const int WaveOutLatencyMilliseconds = 120;

    private readonly ManualResetEventSlim _threadStopped = new(true);
    private IWavePlayer? _player;
    private IWavePosition? _position;
    private int _sourceRate;
    private long _bytesPerSecond;
    private long _lastPlayedFrames;
    private volatile bool _stopRequested = true;
    private bool _disposed;

    public event EventHandler<Exception?>? Failed;

    /// <summary>Which API is in use (for logs): "WASAPI" or "WaveOut".</summary>
    public string? Api { get; private set; }

    public long PlayedFrames
    {
        get
        {
            IWavePosition? position = _position;
            if (position is null || _bytesPerSecond <= 0)
            {
                return 0;
            }

            try
            {
                long bytes = position.GetPosition();
                long frames = bytes * _sourceRate / _bytesPerSecond;
                _lastPlayedFrames = frames;
                return frames;
            }
            catch (Exception)
            {
                // Device gone mid-call: the Failed event reports it; keep the clock where it was.
                return _lastPlayedFrames;
            }
        }
    }

    public void Init(ISampleProvider source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_player is not null)
        {
            throw new InvalidOperationException("The output is already initialised.");
        }

        _sourceRate = source.WaveFormat.SampleRate;

        // Without a SynchronizationContext NAudio raises PlaybackStopped directly on its own thread, so Stop() can wait
        // for it and nothing depends on the UI thread pumping messages.
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Exception? wasapiError = TryInit(
                () => new WasapiOut(AudioClientShareMode.Shared, true, WasapiLatencyMilliseconds),
                new SampleToWaveProvider(source),
                "WASAPI");
            if (wasapiError is null)
            {
                return;
            }

            Exception? waveOutError = TryInit(
                () => new WaveOutEvent { DesiredLatency = WaveOutLatencyMilliseconds, NumberOfBuffers = 3 },
                new SampleToWaveProvider16(source), // 16-bit: accepted by every WinMM driver
                "WaveOut");
            if (waveOutError is null)
            {
                return;
            }

            throw new AudioException(
                $"No audio output device is available: {wasapiError.Message}", new AggregateException(wasapiError, waveOutError));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public void Play()
    {
        IWavePlayer player = _player ?? throw new InvalidOperationException("Init must be called first.");
        _stopRequested = false;
        _threadStopped.Reset();
        _lastPlayedFrames = 0;
        try
        {
            player.Play();
        }
        catch (Exception ex)
        {
            _stopRequested = true;
            _threadStopped.Set();
            throw new AudioException($"The audio device could not start: {ex.Message}", ex);
        }
    }

    public void Stop()
    {
        IWavePlayer? player = _player;
        if (player is null)
        {
            return;
        }

        _stopRequested = true;
        try
        {
            player.Stop();
        }
        catch (Exception)
        {
            // Stop on a removed device can fail; the device is gone either way.
        }

        // WasapiOut.Stop joins its thread; WaveOutEvent's thread ends asynchronously and must be gone before a new Play.
        _threadStopped.Wait(TimeSpan.FromSeconds(2));
        _lastPlayedFrames = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        IWavePlayer? player = _player;
        _player = null;
        _position = null;
        if (player is not null)
        {
            player.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                player.Dispose();
            }
            catch (Exception)
            {
                // Releasing a removed device can throw; nothing is left to release.
            }
        }

        _threadStopped.Dispose();
    }

    private Exception? TryInit(Func<IWavePlayer> create, IWaveProvider provider, string api)
    {
        IWavePlayer? player = null;
        try
        {
            player = create();
            player.Init(provider);
            if (player is not IWavePosition position || position.OutputWaveFormat.AverageBytesPerSecond <= 0)
            {
                throw new NotSupportedException($"{api} does not report its play position.");
            }

            player.PlaybackStopped += OnPlaybackStopped;
            _player = player;
            _position = position;
            _bytesPerSecond = position.OutputWaveFormat.AverageBytesPerSecond;
            Api = api;
            return null;
        }
        catch (Exception ex)
        {
            try
            {
                player?.Dispose();
            }
            catch (Exception)
            {
                // Best effort clean-up of a half-initialised device; the original error is reported.
            }

            return ex;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool unexpected = !_stopRequested;
        _stopRequested = true;
        _threadStopped.Set();
        if (unexpected || e.Exception is not null)
        {
            Failed?.Invoke(this, e.Exception);
        }
    }
}
