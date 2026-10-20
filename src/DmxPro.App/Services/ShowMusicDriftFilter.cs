namespace DmxPro.App.Services;

/// <summary>
/// Decides when the lights have drifted from the music: the median of the last <see cref="WindowSize"/> drift samples
/// (music show time − estimated engine show time) must exceed <see cref="ThresholdSeconds"/>. The median ignores single
/// outliers (a late timer tick, a coarse audio position). Pure and allocation-free.
/// </summary>
internal sealed class ShowMusicDriftFilter
{
    /// <summary>Samples needed before a decision (~250 ms at the 40 Hz sync tick).</summary>
    public const int WindowSize = 10;

    /// <summary>Drift that is corrected (lights vs. music sync perception starts around 40–45 ms).</summary>
    public const double ThresholdSeconds = 0.025;

    private readonly double[] _samples = new double[WindowSize];
    private readonly double[] _sorted = new double[WindowSize];
    private int _count;
    private int _next;

    public void Reset()
    {
        _count = 0;
        _next = 0;
    }

    public void Add(double driftSeconds)
    {
        if (!double.IsFinite(driftSeconds))
        {
            return;
        }

        _samples[_next] = driftSeconds;
        _next = (_next + 1) % WindowSize;
        _count = Math.Min(WindowSize, _count + 1);
    }

    /// <summary>True when the window is full and its median drift is beyond the threshold (either way).</summary>
    public bool TryGetCorrection(out double driftSeconds)
    {
        driftSeconds = 0;
        if (_count < WindowSize)
        {
            return false;
        }

        Array.Copy(_samples, _sorted, WindowSize);
        Array.Sort(_sorted);
        driftSeconds = (_sorted[(WindowSize / 2) - 1] + _sorted[WindowSize / 2]) / 2;
        return Math.Abs(driftSeconds) > ThresholdSeconds;
    }
}
