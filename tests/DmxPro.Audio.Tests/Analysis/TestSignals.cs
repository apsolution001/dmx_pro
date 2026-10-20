namespace DmxPro.Audio.Tests.Analysis;

/// <summary>Deterministic synthetic signals for analysis tests.</summary>
internal static class TestSignals
{
    /// <summary>
    /// A click track: each beat is a short percussive burst (decaying 1 kHz tone plus a decaying noise transient),
    /// optionally preceded by silence and mixed with white noise of the given peak amplitude.
    /// </summary>
    public static float[] ClickTrack(double bpm, double seconds, int sampleRate, double introSeconds = 0, float noiseAmplitude = 0, int seed = 1)
    {
        var samples = new float[(int)(seconds * sampleRate)];
        var random = new Random(seed);
        if (noiseAmplitude > 0)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = noiseAmplitude * (float)((random.NextDouble() * 2) - 1);
            }
        }

        var period = 60.0 / bpm;
        var clickLength = (int)(0.06 * sampleRate);
        for (var beat = introSeconds; beat < seconds; beat += period)
        {
            var start = (int)Math.Round(beat * sampleRate);
            for (var j = 0; j < clickLength && start + j < samples.Length; j++)
            {
                var t = (double)j / sampleRate;
                var tone = Math.Sin(2 * Math.PI * 1000 * t) * Math.Exp(-t / 0.015);
                var transient = ((random.NextDouble() * 2) - 1) * Math.Exp(-t / 0.004);
                samples[start + j] += (float)(0.6 * tone + 0.3 * transient);
            }
        }

        return samples;
    }

    public static float[] WhiteNoise(double seconds, int sampleRate, float amplitude = 0.5f, int seed = 7)
    {
        var random = new Random(seed);
        var samples = new float[(int)(seconds * sampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = amplitude * (float)((random.NextDouble() * 2) - 1);
        }

        return samples;
    }

    public static float[] Sine(double frequency, double seconds, int sampleRate, float amplitude = 0.5f)
    {
        var samples = new float[(int)(seconds * sampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
        }

        return samples;
    }
}
