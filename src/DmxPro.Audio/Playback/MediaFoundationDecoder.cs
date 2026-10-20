using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;

namespace DmxPro.Audio.Playback;

/// <summary>
/// <see cref="ISampleDecoder"/> on the Windows Media Foundation source reader (MP3, WAV, M4A/AAC, FLAC on Windows 10+).
/// Unlike NAudio's MediaFoundationReader it uses the sample timestamps after a seek, so a seek lands on the exact
/// frame (decoders seek to a packet boundary before the target; the extra frames are dropped), which keeps lights in
/// sync after every jump. Must be created on an MTA thread (any thread-pool thread) so the audio thread, also MTA, can
/// use the COM objects directly without marshalling.
/// </summary>
internal sealed class MediaFoundationDecoder : ISampleDecoder
{
    private const int FirstAudioStream = MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM;
    private const long HnsPerSecond = 10_000_000;
    private static readonly object StartupLock = new();

    private readonly string _fileName;
    private readonly SampleEncoding _encoding;
    private readonly int _bytesPerFrame;
    private readonly IntPtr _positionVariant;
    private IMFSourceReader? _reader;
    private byte[] _pending = new byte[64 * 1024];
    private int _pendingOffset;
    private int _pendingCount;
    private long _nextFrame;
    private long _seekTarget = -1;
    private bool _ended;

    private MediaFoundationDecoder(IMFSourceReader reader, string fileName, int sampleRate, int channels, SampleEncoding encoding)
    {
        _reader = reader;
        _fileName = fileName;
        SampleRate = sampleRate;
        Channels = channels;
        _encoding = encoding;
        _bytesPerFrame = BytesPerSample(encoding) * channels;
        _positionVariant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
    }

    private enum SampleEncoding
    {
        Float32,
        Pcm8,
        Pcm16,
        Pcm24,
        Pcm32,
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public long LengthFrames { get; private set; }

    public long NextFrame => _seekTarget >= 0 ? _seekTarget : _nextFrame;

    /// <summary>Opens <paramref name="path"/> and decodes the first block to prove it plays.</summary>
    /// <exception cref="AudioException">Missing, unsupported or damaged file, or Media Foundation unavailable.</exception>
    public static MediaFoundationDecoder Open(string path)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            throw new AudioException($"This file cannot be played: \"{name}\" was not found.");
        }

        try
        {
            lock (StartupLock)
            {
                MediaFoundationApi.Startup();
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            throw new AudioException(
                "This file cannot be played: Windows Media Foundation is not available (Windows N editions need the Media Feature Pack).",
                ex);
        }

        IMFSourceReader? reader = null;
        MediaFoundationDecoder? decoder = null;
        try
        {
            MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out reader);
            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(FirstAudioStream, true);
            ConfigureOutputType(reader);
            (int rate, int channels, SampleEncoding encoding) = ReadCurrentFormat(reader);
            decoder = new MediaFoundationDecoder(reader, name, rate, channels, encoding);
            reader = null; // owned by the decoder now
            decoder.LengthFrames = decoder.ReadLengthFrames();

            // Decode the first block now: a damaged file fails here, at load, not later in the middle of a show.
            if (!decoder.FillPending())
            {
                throw new AudioException($"This file cannot be played: \"{name}\" contains no audio.");
            }

            return decoder;
        }
        catch (AudioException)
        {
            decoder?.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or ArgumentException)
        {
            decoder?.Dispose();
            throw new AudioException($"This file cannot be played: \"{name}\" is damaged or its format is not supported.", ex);
        }
        finally
        {
            if (reader is not null)
            {
                Marshal.ReleaseComObject(reader);
            }
        }
    }

    public int Read(Span<float> destination, out long firstFrame)
    {
        int wanted = destination.Length / Channels;
        if (wanted == 0 || (_pendingCount == 0 && !FillPending()))
        {
            firstFrame = _nextFrame;
            return 0;
        }

        firstFrame = _nextFrame;
        int frames = Math.Min(wanted, _pendingCount / _bytesPerFrame);
        int bytes = frames * _bytesPerFrame;
        Convert(_pending.AsSpan(_pendingOffset, bytes), destination[..(frames * Channels)]);
        _pendingOffset += bytes;
        _pendingCount -= bytes;
        _nextFrame += frames;
        if (_nextFrame > LengthFrames)
        {
            LengthFrames = _nextFrame;
        }

        return frames;
    }

    public void Seek(long frame)
    {
        IMFSourceReader reader = _reader ?? throw new ObjectDisposedException(nameof(MediaFoundationDecoder));
        frame = Math.Max(0, frame);
        long hns = (long)Math.Round(frame * (double)HnsPerSecond / SampleRate);
        Marshal.StructureToPtr(PropVariant.FromLong(hns), _positionVariant, false);
        reader.SetCurrentPosition(Guid.Empty, _positionVariant);
        _pendingOffset = 0;
        _pendingCount = 0;
        _ended = false;
        _seekTarget = frame;
        _nextFrame = frame;
    }

    public void Dispose()
    {
        if (_reader is null)
        {
            return;
        }

        Marshal.ReleaseComObject(_reader);
        _reader = null;
        Marshal.FreeHGlobal(_positionVariant);
    }

    private static int BytesPerSample(SampleEncoding encoding) => encoding switch
    {
        SampleEncoding.Pcm8 => 1,
        SampleEncoding.Pcm16 => 2,
        SampleEncoding.Pcm24 => 3,
        _ => 4,
    };

    private static void ConfigureOutputType(IMFSourceReader reader)
    {
        reader.GetCurrentMediaType(FirstAudioStream, out IMFMediaType nativeObject);
        var native = new MediaType(nativeObject);
        int nativeRate = native.SampleRate;
        int nativeChannels = native.ChannelCount;
        Marshal.ReleaseComObject(nativeObject);

        // Float keeps full quality; plain PCM (e.g. WAV, where no decoder is involved) is converted here instead.
        // Naming the native rate/channels avoids odd defaults for mono / low-rate files; leaving them out lets HE-AAC
        // (which reports half its real rate) pick its own.
        if (TrySetOutputType(reader, AudioSubtypes.MFAudioFormat_Float, nativeRate, nativeChannels)
            || TrySetOutputType(reader, AudioSubtypes.MFAudioFormat_PCM, nativeRate, nativeChannels)
            || TrySetOutputType(reader, AudioSubtypes.MFAudioFormat_Float, 0, 0)
            || TrySetOutputType(reader, AudioSubtypes.MFAudioFormat_PCM, 0, 0))
        {
            return;
        }

        throw new AudioException("This file cannot be played: no decoder for its audio format is installed.");
    }

    private static bool TrySetOutputType(IMFSourceReader reader, Guid subType, int sampleRate, int channels)
    {
        var partial = new MediaType { MajorType = MediaTypes.MFMediaType_Audio, SubType = subType };
        try
        {
            if (sampleRate > 0)
            {
                partial.SampleRate = sampleRate;
                partial.ChannelCount = channels;
            }

            reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, partial.MediaFoundationObject);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(partial.MediaFoundationObject);
        }
    }

    private static (int Rate, int Channels, SampleEncoding Encoding) ReadCurrentFormat(IMFSourceReader reader)
    {
        reader.GetCurrentMediaType(FirstAudioStream, out IMFMediaType currentObject);
        try
        {
            var current = new MediaType(currentObject);
            int rate = current.SampleRate;
            int channels = current.ChannelCount;
            Guid subType = current.SubType;
            int bits = current.BitsPerSample;
            if (rate <= 0 || channels <= 0)
            {
                throw new AudioException("This file cannot be played: its sample rate or channel count is invalid.");
            }

            SampleEncoding? encoding = subType == AudioSubtypes.MFAudioFormat_Float && bits == 32 ? SampleEncoding.Float32
                : subType != AudioSubtypes.MFAudioFormat_PCM ? null
                : bits switch
                {
                    8 => SampleEncoding.Pcm8,
                    16 => SampleEncoding.Pcm16,
                    24 => SampleEncoding.Pcm24,
                    32 => SampleEncoding.Pcm32,
                    _ => null,
                };
            return encoding is { } e
                ? (rate, channels, e)
                : throw new AudioException("This file cannot be played: its decoded sample format is not supported.");
        }
        finally
        {
            Marshal.ReleaseComObject(currentObject);
        }
    }

    private long ReadLengthFrames()
    {
        IntPtr variant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            int hr = _reader!.GetPresentationAttribute(
                MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE, MediaFoundationAttributes.MF_PD_DURATION, variant);
            if (hr != 0)
            {
                return 0; // unknown length (e.g. a stream); grows while decoding
            }

            long hns = Marshal.PtrToStructure<PropVariant>(variant).hVal;
            return (long)Math.Round(hns * (double)SampleRate / HnsPerSecond);
        }
        finally
        {
            PropVariant.Clear(variant);
            Marshal.FreeHGlobal(variant);
        }
    }

    /// <summary>Reads the next media sample into the pending buffer; false at the end of the stream.</summary>
    private bool FillPending()
    {
        IMFSourceReader reader = _reader ?? throw new ObjectDisposedException(nameof(MediaFoundationDecoder));
        while (!_ended)
        {
            reader.ReadSample(FirstAudioStream, 0, out _, out MF_SOURCE_READER_FLAG flags, out ulong timestamp, out IMFSample? sample);
            try
            {
                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
                {
                    throw new AudioException($"This file cannot be played: \"{_fileName}\" could not be decoded.");
                }

                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                {
                    (int rate, int channels, SampleEncoding encoding) = ReadCurrentFormat(reader);
                    if (rate != SampleRate || channels != Channels || encoding != _encoding)
                    {
                        throw new AudioException($"This file cannot be played: \"{_fileName}\" changes its audio format mid-song.");
                    }
                }

                if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    _ended = true;
                }

                if (sample is not null && TakeSample(sample, (long)timestamp))
                {
                    return true;
                }
            }
            finally
            {
                if (sample is not null)
                {
                    Marshal.ReleaseComObject(sample);
                }
            }
        }

        return false;
    }

    /// <summary>Copies a media sample into the pending buffer, dropping frames before a seek target.</summary>
    private bool TakeSample(IMFSample sample, long timestamp)
    {
        sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        try
        {
            buffer.Lock(out IntPtr data, out _, out int length);
            try
            {
                int frames = length / _bytesPerFrame;
                if (frames == 0)
                {
                    return false;
                }

                int skip = 0;
                if (_seekTarget >= 0)
                {
                    long sampleFrame = (long)Math.Round(timestamp * (double)SampleRate / HnsPerSecond);
                    if (sampleFrame + frames <= _seekTarget)
                    {
                        return false; // wholly before the target
                    }

                    if (sampleFrame < _seekTarget)
                    {
                        skip = (int)(_seekTarget - sampleFrame);
                        _nextFrame = _seekTarget;
                    }
                    else
                    {
                        // Landed on or after the target: report where the audio really is.
                        _nextFrame = sampleFrame;
                    }

                    _seekTarget = -1;
                }

                int bytes = (frames - skip) * _bytesPerFrame;
                if (_pending.Length < bytes)
                {
                    _pending = new byte[bytes];
                }

                Marshal.Copy(data + (skip * _bytesPerFrame), _pending, 0, bytes);
                _pendingOffset = 0;
                _pendingCount = bytes;
                return true;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    private void Convert(ReadOnlySpan<byte> source, Span<float> destination)
    {
        switch (_encoding)
        {
            case SampleEncoding.Float32:
                MemoryMarshal.Cast<byte, float>(source).CopyTo(destination);
                break;
            case SampleEncoding.Pcm16:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 2)..]) / 32768f;
                }

                break;
            case SampleEncoding.Pcm24:
                for (int i = 0; i < destination.Length; i++)
                {
                    int o = i * 3;
                    int value = (source[o] << 8) | (source[o + 1] << 16) | (source[o + 2] << 24);
                    destination[i] = (value >> 8) / 8388608f;
                }

                break;
            case SampleEncoding.Pcm32:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt32LittleEndian(source[(i * 4)..]) / 2147483648f;
                }

                break;
            default:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = (source[i] - 128) / 128f;
                }

                break;
        }
    }
}
