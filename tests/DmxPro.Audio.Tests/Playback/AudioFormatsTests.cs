namespace DmxPro.Audio.Tests.Playback;

public class AudioFormatsTests
{
    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.WAV")]
    [InlineData(@"C:\Music\My Song.m4a")]
    [InlineData("song.aac")]
    [InlineData("song.FlAc")]
    public void IsSupported_MusicExtension_ReturnsTrue(string path) => Assert.True(AudioFormats.IsSupported(path));

    [Theory]
    [InlineData("song.ogg")]
    [InlineData("song.wma")]
    [InlineData("song")]
    [InlineData("song.mp3.txt")]
    [InlineData("")]
    public void IsSupported_OtherFile_ReturnsFalse(string path) => Assert.False(AudioFormats.IsSupported(path));

    [Fact]
    public void DialogFilter_ListsEveryExtension()
    {
        foreach (string extension in AudioFormats.Extensions)
        {
            Assert.Contains("*" + extension, AudioFormats.DialogFilter);
        }
    }
}
