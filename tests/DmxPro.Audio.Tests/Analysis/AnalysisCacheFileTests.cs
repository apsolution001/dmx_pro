using System.Buffers.Binary;
using DmxPro.Audio.Analysis;

namespace DmxPro.Audio.Tests.Analysis;

public class AnalysisCacheFileTests
{
    private static readonly AudioFileIdentity Source = new(@"C:\Music\Song.mp3", 1234567, 638000000000000000);

    private static readonly AudioAnalysis Sample = new(185.25, AudioAnalyzer.PeaksPerSecond, [0f, 0.5f, 1f, 0.25f], 128.0, 0.42);

    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        var bytes = AnalysisCacheFile.Serialize(Sample, Source);

        var result = AnalysisCacheFile.Deserialize(bytes, Source, out var reason);

        Assert.Null(reason);
        Assert.NotNull(result);
        Assert.Equal(Sample.DurationSeconds, result.DurationSeconds);
        Assert.Equal(Sample.PeaksPerSecond, result.PeaksPerSecond);
        Assert.Equal(Sample.Peaks, result.Peaks);
        Assert.Equal(Sample.Bpm, result.Bpm);
        Assert.Equal(Sample.FirstBeatSeconds, result.FirstBeatSeconds);
    }

    [Fact]
    public void SerializeDeserialize_NoBpm_RoundTripsNull()
    {
        var analysis = Sample with { Bpm = null, FirstBeatSeconds = 0, Peaks = [] };

        var result = AnalysisCacheFile.Deserialize(AnalysisCacheFile.Serialize(analysis, Source), Source, out _);

        Assert.NotNull(result);
        Assert.Null(result.Bpm);
        Assert.Empty(result.Peaks);
    }

    [Fact]
    public void Serialize_Layout_HeaderBytesAsDocumented()
    {
        var bytes = AnalysisCacheFile.Serialize(Sample, Source);

        Assert.Equal("DMXA"u8.ToArray(), bytes[..4]);
        Assert.Equal(AudioAnalyzer.AnalysisVersion, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(Source.Size, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(Source.LastWriteTicks, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(56)));
        Assert.Equal(60 + (4 * 4) + 32, bytes.Length);
    }

    [Fact]
    public void Deserialize_FlippedByte_ChecksumMismatch()
    {
        var bytes = AnalysisCacheFile.Serialize(Sample, Source);
        bytes[62] ^= 0x40;

        Assert.Null(AnalysisCacheFile.Deserialize(bytes, Source, out var reason));
        Assert.Contains("checksum", reason);
    }

    [Fact]
    public void Deserialize_Truncated_WrongLength()
    {
        var bytes = AnalysisCacheFile.Serialize(Sample, Source);

        Assert.Null(AnalysisCacheFile.Deserialize(bytes.AsSpan(0, bytes.Length - 5), Source, out var reason));
        Assert.Contains("length", reason);
    }

    [Fact]
    public void Deserialize_OtherVersion_Rejected()
    {
        var bytes = AnalysisCacheFile.Serialize(Sample, Source);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), AudioAnalyzer.AnalysisVersion + 1);

        Assert.Null(AnalysisCacheFile.Deserialize(bytes, Source, out var reason));
        Assert.Contains("version", reason);
    }

    [Fact]
    public void Deserialize_DifferentSourceFile_Rejected()
    {
        var bytes = AnalysisCacheFile.Serialize(Sample, Source);

        Assert.Null(AnalysisCacheFile.Deserialize(bytes, Source with { Size = 1 }, out var reason));
        Assert.Contains("source", reason);
    }

    [Fact]
    public void Deserialize_Garbage_Rejected()
    {
        Assert.Null(AnalysisCacheFile.Deserialize("not a cache"u8, Source, out _));
        Assert.Null(AnalysisCacheFile.Deserialize([], Source, out _));
    }

    [Fact]
    public void FileName_DependsOnPathCaseInsensitivelySizeAndTime()
    {
        var name = AnalysisCacheFile.FileName(Source);

        Assert.EndsWith(AnalysisCacheFile.Extension, name);
        Assert.Equal(name, AnalysisCacheFile.FileName(Source with { FullPath = Source.FullPath.ToUpperInvariant() }));
        Assert.NotEqual(name, AnalysisCacheFile.FileName(Source with { Size = Source.Size + 1 }));
        Assert.NotEqual(name, AnalysisCacheFile.FileName(Source with { LastWriteTicks = Source.LastWriteTicks + 1 }));
        Assert.NotEqual(name, AnalysisCacheFile.FileName(Source with { FullPath = @"C:\Music\Other.mp3" }));
    }

    [Fact]
    public void WriteAtomic_CreatesFolderAndLeavesNoTempFiles()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "sub", "a.dmxa");

        AnalysisCacheFile.WriteAtomic(path, [1, 2, 3]);
        AnalysisCacheFile.WriteAtomic(path, [4, 5]);

        Assert.Equal([4, 5], File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(Path.Combine(folder.Path, "sub")));
    }
}
