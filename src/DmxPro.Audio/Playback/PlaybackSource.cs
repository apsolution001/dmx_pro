using NAudio.Wave;

namespace DmxPro.Audio.Playback;

/// <summary>
/// The sample chain between the decoder and the output device: decoder → <see cref="PlaybackSampleProcessor"/> (volume
/// ramp, transition ramps, fade-out window) → device. It also records which song frame every output frame carries
/// (<see cref="HeardPositionMap"/>) so the player can tell what is being heard right now.
/// <para>
/// Transport is command based: <see cref="Run"/> (play from a frame) and <see cref="Halt"/> (go silent) are queued by the
/// player and executed at the start of the next <see cref="Read"/> on the audio thread, so the decoder is only ever
/// touched by one thread. While audio is audible a command first fades the current audio out over a few ms, then
/// executes; a Run then fades in. Seeks and pauses are therefore click-free and the decoder never races a reposition.
/// </para>
/// <para>
/// Locking: <c>_decodeLock</c> is held for a whole <see cref="Read"/> (and by <see cref="Dispose"/>); <c>_sync</c> is held
/// only for short field copies and guards everything other threads touch. Order: _decodeLock → _sync. Read never
/// throws: a decode failure turns into silence plus one <see cref="DecodeFailed"/> callback.
/// </para>
/// </summary>
internal sealed class PlaybackSource : ISampleProvider, IDisposable
{
    private readonly object _decodeLock = new();
    private readonly object _sync = new();
    private readonly ISampleDecoder _decoder;
    private readonly PlaybackSampleProcessor _processor;
    private readonly HeardPositionMap _map;
    private readonly int _channels;

    // Guarded by _sync (written by the player).
    private Command _pendingCommand;
    private long _pendingFrame;
    private int _pendingGeneration;
    private float _volume = 1f;
    private long _fadeStart;
    private long _fadeLength;
    private bool _parametersChanged = true;
    private bool _silenceNow;
    private long _lengthFrames;

    // Audio thread only (under _decodeLock).
    private Mode _mode = Mode.Halted;
    private int _generation;
    private long _holdFrame;
    private bool _disposed;
    private bool _failed;

    public PlaybackSource(ISampleDecoder decoder, int mapCapacity = 1024)
    {
        _decoder = decoder;
        _channels = decoder.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(decoder.SampleRate, decoder.Channels);
        _processor = new PlaybackSampleProcessor(decoder.SampleRate, decoder.Channels);
        _map = new HeardPositionMap(mapCapacity);
        _lengthFrames = Math.Max(0, decoder.LengthFrames);
        _holdFrame = 0;
    }

    private enum Command
    {
        None,
        Run,
        Halt,
    }

    private enum Mode
    {
        Halted,
        Audio,
        Ended,
    }

    public WaveFormat WaveFormat { get; }

    public int SampleRate => WaveFormat.SampleRate;

    /// <summary>Song length in frames: the file's value, grown if decoding runs past it.</summary>
    public long LengthFrames
    {
        get
        {
            lock (_sync)
            {
                return _lengthFrames;
            }
        }
    }

    /// <summary>Raised once, on the audio thread, when decoding fails mid-song. Handlers must return quickly.</summary>
    public Action<Exception>? DecodeFailed { get; set; }

    /// <summary>Play from song frame <paramref name="frame"/>; output carries <paramref name="generation"/> from then on.</summary>
    public void Run(long frame, int generation)
    {
        lock (_sync)
        {
            _pendingCommand = Command.Run;
            _pendingFrame = Math.Max(0, frame);
            _pendingGeneration = generation;
        }
    }

    /// <summary>Fade out and output silence.</summary>
    public void Halt()
    {
        lock (_sync)
        {
            _pendingCommand = Command.Halt;
        }
    }

    /// <summary>
    /// Goes silent at once, without a fade (the device that played the audio is gone, so there is nothing to fade). A
    /// later <see cref="Run"/> then starts straight with its fade-in.
    /// </summary>
    public void SilenceImmediately()
    {
        lock (_sync)
        {
            _silenceNow = true;
            if (_pendingCommand == Command.Halt)
            {
                _pendingCommand = Command.None;
            }
        }
    }

    public void SetVolume(float volume)
    {
        lock (_sync)
        {
            _volume = PlaybackSampleProcessor.ClampVolume(volume);
            _parametersChanged = true;
        }
    }

    public void SetFadeOut(long startFrame, long lengthFrames)
    {
        lock (_sync)
        {
            _fadeStart = startFrame;
            _fadeLength = lengthFrames;
            _parametersChanged = true;
        }
    }

    /// <summary>The device restarted at output frame 0. Only call while the device is not reading.</summary>
    public void ResetOutputCounter()
    {
        lock (_sync)
        {
            _map.Reset();
        }
    }

    /// <summary>Looks up the song frame carried by device output frame <paramref name="outputFrame"/>.</summary>
    public bool TryMapOutputFrame(long outputFrame, out HeardFrame frame)
    {
        lock (_sync)
        {
            return _map.TryMap(outputFrame, out frame);
        }
    }

    /// <summary>First output frame of the current trailing silence, or -1 while audio is (or will be) produced.</summary>
    public long SilentSinceOutputFrame
    {
        get
        {
            lock (_sync)
            {
                return _pendingCommand == Command.Run ? -1 : _map.SilentSinceOutputFrame;
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / _channels;
        int total = frames * _channels;
        lock (_decodeLock)
        {
            if (_disposed)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            // A queued command is only peeked here and taken (atomically) when it actually executes: the fade-out of the
            // old audio may use up this whole read (small device periods), and a command taken now would then be lost.
            bool commandPending = ApplyChangesAndPeekCommand();
            int done = 0;
            while (done < frames)
            {
                Span<float> destination = buffer.AsSpan(offset + (done * _channels), (frames - done) * _channels);
                if (commandPending)
                {
                    if (_mode == Mode.Audio && !_processor.IsTransitionSilent)
                    {
                        // Finish the current audio with a short fade before jumping or going silent.
                        _processor.BeginFadeOut();
                        int fadeFrames = Math.Min(frames - done, _processor.FramesUntilSilent);
                        int n = DecodeAudio(destination[..(fadeFrames * _channels)]);
                        if (n > 0)
                        {
                            done += n;
                            continue;
                        }
                    }

                    // Take the newest command only now: whatever arrived during the fade replaces the older one.
                    Command command = TakeCommand(out long commandFrame, out int commandGeneration);
                    if (command != Command.None)
                    {
                        Execute(command, commandFrame, commandGeneration);
                    }

                    commandPending = false;
                    continue;
                }

                if (_mode == Mode.Audio)
                {
                    int n = DecodeAudio(destination);
                    if (n > 0)
                    {
                        done += n;
                    }

                    continue;
                }

                destination.Clear();
                lock (_sync)
                {
                    _map.Record(_generation, _mode == Mode.Ended ? OutputSpanKind.End : OutputSpanKind.Halted, _holdFrame, frames - done);
                }

                done = frames;
            }
        }

        if (total < count)
        {
            Array.Clear(buffer, offset + total, count - total);
        }

        return count;
    }

    public void Dispose()
    {
        lock (_decodeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _decoder.Dispose();
        }
    }

    /// <summary>Applies volume / fade / silence changes made by the player; true when a transport command is queued.</summary>
    private bool ApplyChangesAndPeekCommand()
    {
        lock (_sync)
        {
            if (_parametersChanged)
            {
                _processor.TargetVolume = _volume;
                _processor.SetFadeOut(_fadeStart, _fadeLength);
                _parametersChanged = false;
            }

            if (_silenceNow)
            {
                _silenceNow = false;
                _processor.SilenceTransition();
                if (_mode == Mode.Audio)
                {
                    _mode = Mode.Halted;
                    _holdFrame = _decoder.NextFrame;
                }
            }

            return _pendingCommand != Command.None;
        }
    }

    /// <summary>Removes and returns the queued command (the newest one: a later Run / Halt overwrites an earlier one).</summary>
    private Command TakeCommand(out long frame, out int generation)
    {
        lock (_sync)
        {
            Command command = _pendingCommand;
            frame = _pendingFrame;
            generation = _pendingGeneration;
            _pendingCommand = Command.None;
            return command;
        }
    }

    private void Execute(Command command, long frame, int generation)
    {
        if (command == Command.Halt)
        {
            if (_mode == Mode.Audio)
            {
                _mode = Mode.Halted;
                _holdFrame = _decoder.NextFrame;
            }

            return;
        }

        _generation = generation;
        if (_failed)
        {
            _mode = Mode.Ended;
            _holdFrame = frame;
            return;
        }

        try
        {
            if (frame != _decoder.NextFrame || _mode == Mode.Ended)
            {
                _decoder.Seek(frame);
            }
        }
        catch (Exception ex)
        {
            Fail(ex, frame);
            return;
        }

        _mode = Mode.Audio;
        _holdFrame = frame;
        _processor.SnapVolume();
        _processor.BeginFadeIn();
    }

    /// <summary>Decodes, processes and records up to <paramref name="destination"/>; 0 when the song ended.</summary>
    private int DecodeAudio(Span<float> destination)
    {
        int n;
        long first;
        try
        {
            n = _decoder.Read(destination, out first);
        }
        catch (Exception ex)
        {
            Fail(ex, _decoder.NextFrame);
            return 0;
        }

        if (n <= 0)
        {
            _mode = Mode.Ended;
            _holdFrame = first;
            return 0;
        }

        Span<float> produced = destination[..(n * _channels)];
        _processor.Process(produced, first);
        lock (_sync)
        {
            _map.Record(_generation, OutputSpanKind.Audio, first, n);
            if (first + n > _lengthFrames)
            {
                _lengthFrames = first + n;
            }
        }

        return n;
    }

    private void Fail(Exception ex, long frame)
    {
        _mode = Mode.Ended;
        _holdFrame = frame;
        if (_failed)
        {
            return;
        }

        _failed = true;
        try
        {
            DecodeFailed?.Invoke(ex);
        }
        catch (Exception)
        {
            // A faulty handler must never break the audio thread.
        }
    }
}
