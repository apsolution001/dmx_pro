using System.Text.Json.Nodes;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class ShowAudioSerializationTests
{
    private readonly ProjectSerializer _serializer = new();

    [Fact]
    public void Deserialize_ShowWithAudio_RoundTripsAllAudioData()
    {
        var project = new Project();
        var show = project.CreateShow("Wedding");
        show.BeatOffsetSeconds = 0.42;
        show.Audio = new ShowAudio
        {
            FilePath = @"C:\Music\first dance.flac",
            RelativePath = @"..\Music\first dance.flac",
            OffsetSeconds = -1.5,
            Volume = 0.8,
            Muted = true,
            FadeOutSeconds = 6,
            DurationSeconds = 215.25,
            DetectedBpm = 124.5,
        };

        var loaded = Assert.Single(_serializer.Deserialize(_serializer.Serialize(project)).Project.Shows);

        Assert.Equal(0.42, loaded.BeatOffsetSeconds);
        var audio = Assert.IsType<ShowAudio>(loaded.Audio);
        Assert.Equal(@"C:\Music\first dance.flac", audio.FilePath);
        Assert.Equal(@"..\Music\first dance.flac", audio.RelativePath);
        Assert.Equal(-1.5, audio.OffsetSeconds);
        Assert.Equal(0.8, audio.Volume);
        Assert.True(audio.Muted);
        Assert.Equal(6, audio.FadeOutSeconds);
        Assert.Equal(215.25, audio.DurationSeconds);
        Assert.Equal(124.5, audio.DetectedBpm);
    }

    [Fact]
    public void Serialize_ShowWithAudio_UsesCamelCaseNames()
    {
        var project = new Project();
        var show = project.CreateShow("S");
        show.BeatOffsetSeconds = 1;
        show.Audio = new ShowAudio { FilePath = "a.mp3", DurationSeconds = 3 };

        var json = JsonNode.Parse(_serializer.Serialize(project))!;
        var showNode = json["shows"]![0]!;

        Assert.Equal(1, showNode["beatOffsetSeconds"]!.GetValue<double>());
        Assert.Equal("a.mp3", showNode["audio"]!["filePath"]!.GetValue<string>());
        Assert.Equal(3, showNode["audio"]!["durationSeconds"]!.GetValue<double>());
    }

    [Fact]
    public void Serialize_ShowWithoutAudio_OmitsAudio()
    {
        var project = new Project();
        project.CreateShow("S");

        var showNode = JsonNode.Parse(_serializer.Serialize(project))!["shows"]![0]!.AsObject();

        Assert.False(showNode.ContainsKey("audio"));
    }

    [Fact]
    public void Deserialize_OldFileWithoutAudioFields_LoadsWithoutMusic()
    {
        var project = new Project();
        project.CreateShow("Old");
        var json = JsonNode.Parse(_serializer.Serialize(project))!;
        var showNode = json["shows"]![0]!.AsObject();
        showNode.Remove("beatOffsetSeconds");
        showNode.Remove("audio");

        var result = _serializer.Deserialize(json.ToJsonString());

        var show = Assert.Single(result.Project.Shows);
        Assert.Null(show.Audio);
        Assert.Equal(0, show.BeatOffsetSeconds);
    }

    [Fact]
    public void Deserialize_AudioWithBadValues_LoadsNormalized()
    {
        var project = new Project();
        project.CreateShow("Bad");
        var json = JsonNode.Parse(_serializer.Serialize(project))!;
        json["shows"]![0]!["audio"] = JsonNode.Parse(
            """{ "filePath": null, "relativePath": "", "volume": 5, "fadeOutSeconds": -3, "durationSeconds": -1, "detectedBpm": 0 }""");

        var result = _serializer.Deserialize(json.ToJsonString());

        var audio = Assert.IsType<ShowAudio>(Assert.Single(result.Project.Shows).Audio);
        Assert.Equal(string.Empty, audio.FilePath);
        Assert.Null(audio.RelativePath);
        Assert.Equal(1, audio.Volume);
        Assert.Equal(0, audio.FadeOutSeconds);
        Assert.Null(audio.DurationSeconds);
        Assert.Null(audio.DetectedBpm);
    }

    [Fact]
    public void Deserialize_AudioWithMissingOptionalFields_UsesDefaults()
    {
        var project = new Project();
        project.CreateShow("Min");
        var json = JsonNode.Parse(_serializer.Serialize(project))!;
        json["shows"]![0]!["audio"] = JsonNode.Parse("""{ "filePath": "song.mp3" }""");

        var audio = Assert.IsType<ShowAudio>(Assert.Single(_serializer.Deserialize(json.ToJsonString()).Project.Shows).Audio);

        Assert.Equal("song.mp3", audio.FilePath);
        Assert.Equal(1, audio.Volume);
        Assert.False(audio.Muted);
        Assert.Equal(0, audio.OffsetSeconds);
        Assert.Null(audio.DurationSeconds);
    }
}
