using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DmxPro.Audio.Analysis;

/// <summary>
/// On-disk cache of one <see cref="AudioAnalysis"/>. Little-endian binary:
/// <code>
///  0  "DMXA"            magic
///  4  int32             format version (<see cref="AudioAnalyzer.AnalysisVersion"/>)
///  8  int64             source file size
/// 16  int64             source last write time (UTC ticks)
/// 24  float64           duration seconds
/// 32  int32             peaks per second
/// 36  int32             flags (bit 0: has BPM)
/// 40  float64           BPM (0 when none)
/// 48  float64           first beat seconds
/// 56  int32             peak count n
/// 60  float32[n]        peaks
/// ..  byte[32]          SHA-256 of everything before it
/// </code>
/// Anything that does not validate (truncated, wrong version, checksum, out-of-range values) is a cache miss.
/// </summary>
internal static class AnalysisCacheFile
{
    public const string Extension = ".dmxa";

    private const int HeaderSize = 60;
    private const int ChecksumSize = 32;
    private const int MaxPeakCount = 100_000_000;
    private static ReadOnlySpan<byte> Magic => "DMXA"u8;

    /// <summary>Cache file name for a source file: hash of (lower-case full path, size, write time, version).</summary>
    public static string FileName(AudioFileIdentity source)
    {
        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{source.FullPath.ToLowerInvariant()}|{source.Size}|{source.LastWriteTicks}|{AudioAnalyzer.AnalysisVersion}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant() + Extension;
    }

    public static byte[] Serialize(AudioAnalysis analysis, AudioFileIdentity source)
    {
        var peaks = analysis.Peaks;
        var payloadLength = HeaderSize + (peaks.Length * sizeof(float));
        var data = new byte[payloadLength + ChecksumSize];
        var span = data.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], AudioAnalyzer.AnalysisVersion);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], source.Size);
        BinaryPrimitives.WriteInt64LittleEndian(span[16..], source.LastWriteTicks);
        BinaryPrimitives.WriteDoubleLittleEndian(span[24..], analysis.DurationSeconds);
        BinaryPrimitives.WriteInt32LittleEndian(span[32..], analysis.PeaksPerSecond);
        BinaryPrimitives.WriteInt32LittleEndian(span[36..], analysis.Bpm.HasValue ? 1 : 0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[40..], analysis.Bpm ?? 0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[48..], analysis.FirstBeatSeconds);
        BinaryPrimitives.WriteInt32LittleEndian(span[56..], peaks.Length);
        for (var i = 0; i < peaks.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[(HeaderSize + (i * sizeof(float)))..], peaks[i]);
        }

        SHA256.HashData(span[..payloadLength], span[payloadLength..]);
        return data;
    }

    /// <summary>Parses cache bytes; null (with a reason) when they are not a valid cache for <paramref name="source"/>.</summary>
    public static AudioAnalysis? Deserialize(ReadOnlySpan<byte> data, AudioFileIdentity source, out string? reason)
    {
        reason = Validate(data, source);
        if (reason is not null)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data[56..]);
        var peaks = new float[count];
        for (var i = 0; i < count; i++)
        {
            peaks[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(HeaderSize + (i * sizeof(float)))..]);
        }

        var hasBpm = (BinaryPrimitives.ReadInt32LittleEndian(data[36..]) & 1) != 0;
        return new AudioAnalysis(
            BinaryPrimitives.ReadDoubleLittleEndian(data[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[32..]),
            peaks,
            hasBpm ? BinaryPrimitives.ReadDoubleLittleEndian(data[40..]) : null,
            BinaryPrimitives.ReadDoubleLittleEndian(data[48..]));
    }

    /// <summary>Writes to a temp file in the same folder, then renames over the target (readers never see half a file).</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(data);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static string? Validate(ReadOnlySpan<byte> data, AudioFileIdentity source)
    {
        if (data.Length < HeaderSize + ChecksumSize || !data[..4].SequenceEqual(Magic))
        {
            return "not a cache file";
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (version != AudioAnalyzer.AnalysisVersion)
        {
            return $"version {version}";
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data[56..]);
        if (count < 0 || count > MaxPeakCount || data.Length != HeaderSize + ((long)count * sizeof(float)) + ChecksumSize)
        {
            return "wrong length";
        }

        var payloadLength = data.Length - ChecksumSize;
        Span<byte> hash = stackalloc byte[ChecksumSize];
        SHA256.HashData(data[..payloadLength], hash);
        if (!hash.SequenceEqual(data[payloadLength..]))
        {
            return "checksum mismatch";
        }

        if (BinaryPrimitives.ReadInt64LittleEndian(data[8..]) != source.Size
            || BinaryPrimitives.ReadInt64LittleEndian(data[16..]) != source.LastWriteTicks)
        {
            return "different source file";
        }

        var duration = BinaryPrimitives.ReadDoubleLittleEndian(data[24..]);
        var peaksPerSecond = BinaryPrimitives.ReadInt32LittleEndian(data[32..]);
        var bpm = BinaryPrimitives.ReadDoubleLittleEndian(data[40..]);
        var hasBpm = (BinaryPrimitives.ReadInt32LittleEndian(data[36..]) & 1) != 0;
        var firstBeat = BinaryPrimitives.ReadDoubleLittleEndian(data[48..]);
        if (!double.IsFinite(duration) || duration < 0
            || peaksPerSecond != AudioAnalyzer.PeaksPerSecond
            || (hasBpm && !(bpm >= BeatDetector.MinBpm && bpm <= BeatDetector.MaxBpm))
            || !double.IsFinite(firstBeat) || firstBeat < 0)
        {
            return "invalid values";
        }

        for (var i = 0; i < count; i++)
        {
            var peak = BinaryPrimitives.ReadSingleLittleEndian(data[(HeaderSize + (i * sizeof(float)))..]);
            if (!(peak >= 0f && peak <= 1f))
            {
                return "invalid peak";
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a leftover .tmp file is harmless and never read as a cache.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
