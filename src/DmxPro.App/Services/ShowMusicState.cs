namespace DmxPro.App.Services;

/// <summary>Music state of a show (see <see cref="IShowMusicService"/>).</summary>
public enum ShowMusicState
{
    /// <summary>The show has no music.</summary>
    None,

    /// <summary>The song is being opened or analysed.</summary>
    Loading,

    /// <summary>The song is found and ready (or playing).</summary>
    Ready,

    /// <summary>The music file was not found at its path; the user must relink it. The show runs without music.</summary>
    Missing,

    /// <summary>The file exists but cannot be played (unsupported, damaged, no audio device). The show runs without music.</summary>
    Error,
}
