namespace DmxPro.Audio.Analysis;

/// <summary>What identifies one version of a music file for caching: full path, size and last write time.</summary>
internal readonly record struct AudioFileIdentity(string FullPath, long Size, long LastWriteTicks)
{
    /// <summary>Reads the identity of an existing file; <see cref="AudioException"/> when it does not exist.</summary>
    public static AudioFileIdentity Of(string path)
    {
        string fullPath;
        FileInfo info;
        try
        {
            fullPath = Path.GetFullPath(path);
            info = new FileInfo(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            throw new AudioException($"\"{path}\" is not a valid file path.", ex);
        }

        if (!info.Exists)
        {
            throw new AudioException($"The music file \"{Path.GetFileName(fullPath)}\" was not found.");
        }

        return new AudioFileIdentity(fullPath, info.Length, info.LastWriteTimeUtc.Ticks);
    }
}
