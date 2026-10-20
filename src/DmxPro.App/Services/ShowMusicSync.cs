namespace DmxPro.App.Services;

/// <summary>
/// Pure decisions of the show music sync (no WPF, no I/O), kept apart so they can be unit-tested: when and where the
/// music joins a running show, and how fast fast-forward/rewind jumps while held.
/// </summary>
internal static class ShowMusicSync
{
    /// <summary>
    /// A join this late after the song start still plays the song from its first sample (and moves the lights back
    /// to match) instead of cutting off the downbeat.
    /// </summary>
    public const double StartWindowSeconds = 0.1;

    /// <summary>Closer than this to the song end there is nothing left worth playing.</summary>
    public const double EndMarginSeconds = 0.05;

    /// <summary>
    /// What the music should do at show time <paramref name="showTimeSeconds"/> (the lights' position now).
    /// </summary>
    /// <param name="showTimeSeconds">Estimated engine show position now.</param>
    /// <param name="offsetSeconds">Show time at which the song starts.</param>
    /// <param name="songDurationSeconds">Song length.</param>
    /// <param name="latencySeconds">Time from Play until the first sample is heard (learned).</param>
    /// <param name="lookAheadSeconds">How early a song start may be triggered (about one sync tick).</param>
    public static ShowMusicJoinPlan PlanJoin(
        double showTimeSeconds,
        double offsetSeconds,
        double songDurationSeconds,
        double latencySeconds,
        double lookAheadSeconds)
    {
        if (!double.IsFinite(showTimeSeconds) || !(songDurationSeconds > 0))
        {
            return new ShowMusicJoinPlan(ShowMusicJoinKind.Wait);
        }

        var offset = double.IsFinite(offsetSeconds) ? offsetSeconds : 0;
        var latency = double.IsFinite(latencySeconds) ? Math.Max(0, latencySeconds) : 0;
        var songTime = showTimeSeconds - offset;

        // Song position that would be heard if Play were called now (it is heard after the output latency).
        var heard = songTime + latency;
        if (heard >= songDurationSeconds - EndMarginSeconds)
        {
            return new ShowMusicJoinPlan(ShowMusicJoinKind.SongOver);
        }

        if (heard < -Math.Max(0, lookAheadSeconds))
        {
            return new ShowMusicJoinPlan(ShowMusicJoinKind.Wait);
        }

        if (heard <= StartWindowSeconds)
        {
            return new ShowMusicJoinPlan(ShowMusicJoinKind.StartAtSongStart, 0, offset - latency);
        }

        return new ShowMusicJoinPlan(ShowMusicJoinKind.JoinMidSong, heard);
    }

    /// <summary>
    /// Jump of a held fast-forward/rewind: the press and the first ~1 s of repeats jump one seek step, then 2×, and
    /// after ~2 s 3× (at 150 ms per repeat).
    /// </summary>
    /// <param name="stepSeconds">The seek step (Settings).</param>
    /// <param name="repeatIndex">0 for the press, then 1, 2, … for each repeat while held.</param>
    public static double FastSeekStep(double stepSeconds, int repeatIndex)
    {
        var step = double.IsFinite(stepSeconds) && stepSeconds > 0 ? stepSeconds : 1;
        return repeatIndex switch
        {
            < 7 => step,
            < 14 => step * 2,
            _ => step * 3,
        };
    }
}
