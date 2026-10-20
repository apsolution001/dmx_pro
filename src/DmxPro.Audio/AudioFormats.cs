namespace DmxPro.Audio;

/// <summary>Music file types a show accepts (owner decision 2026-10-21: MP3, WAV, M4A/AAC, FLAC).</summary>
public static class AudioFormats
{
    /// <summary>Lower-case extensions with the dot.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".mp3", ".wav", ".m4a", ".aac", ".flac"];

    /// <summary>Open-file dialog filter.</summary>
    public static string DialogFilter { get; } =
        "Music (*.mp3;*.wav;*.m4a;*.aac;*.flac)|*.mp3;*.wav;*.m4a;*.aac;*.flac|All files (*.*)|*.*";

    /// <summary>True when the file name has a supported extension (case-insensitive).</summary>
    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
