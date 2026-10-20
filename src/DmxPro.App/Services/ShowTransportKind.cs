namespace DmxPro.App.Services;

/// <summary>What happened to a show's transport (see <see cref="IConsolePlayback.ShowTransport"/>).</summary>
public enum ShowTransportKind
{
    /// <summary>The show was started (from stopped) at <see cref="ShowTransportEventArgs.PositionSeconds"/>.</summary>
    Started,

    /// <summary>The show was paused at <see cref="ShowTransportEventArgs.PositionSeconds"/>.</summary>
    Paused,

    /// <summary>A paused show continues from <see cref="ShowTransportEventArgs.PositionSeconds"/>.</summary>
    Resumed,

    /// <summary>The show jumped to <see cref="ShowTransportEventArgs.PositionSeconds"/> (playing or paused).</summary>
    Seeked,

    /// <summary>The show was stopped by a command (or removed from the project).</summary>
    Stopped,

    /// <summary>The show stopped by itself at its end (seen by the playback poll, up to ~100 ms late).</summary>
    Ended,

    /// <summary>Every show stopped at once (Release All, project opened). <see cref="ShowTransportEventArgs.ShowId"/> is empty.</summary>
    AllStopped,

    /// <summary>The user started scrubbing / fast-seeking the show: music goes silent until <see cref="ScrubEnded"/>.</summary>
    ScrubStarted,

    /// <summary>Scrubbing / fast-seeking ended: music rejoins the show in sync.</summary>
    ScrubEnded,
}
