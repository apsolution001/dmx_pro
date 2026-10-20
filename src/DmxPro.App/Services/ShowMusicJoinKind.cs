namespace DmxPro.App.Services;

/// <summary>What the music should do now (see <see cref="ShowMusicSync.PlanJoin"/>).</summary>
internal enum ShowMusicJoinKind
{
    /// <summary>The song starts later: stay silent; the engine clock is master until then.</summary>
    Wait,

    /// <summary>The song starts now (or just started): play from the song start and align the lights to it.</summary>
    StartAtSongStart,

    /// <summary>Inside the song: play from <see cref="ShowMusicJoinPlan.AudioPositionSeconds"/>.</summary>
    JoinMidSong,

    /// <summary>The song is over at this show time: stay silent.</summary>
    SongOver,
}
