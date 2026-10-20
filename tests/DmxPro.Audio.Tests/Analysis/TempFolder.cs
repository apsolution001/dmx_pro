namespace DmxPro.Audio.Tests.Analysis;

/// <summary>A unique temporary folder, deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmxpro-audio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file may still be open by a cancelled background read; the OS temp cleanup removes it later.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
