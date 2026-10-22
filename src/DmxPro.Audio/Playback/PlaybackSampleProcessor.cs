namespace DmxPro.Audio.Playback;

/// <summary>
/// Applies the three gain stages of the player to interleaved float samples, in place, sample-accurately:
/// <list type="bullet">
/// <item>Smooth volume: the gain moves linearly toward <see cref="TargetVolume"/> by at most 1/<see cref="VolumeRampFrames"/>
/// per frame (a full 0→1 change takes ~20 ms), so volume changes never step (no zipper noise).</item>
/// <item>Transition ramp: a short linear fade-in after a seek / play start, or fade-out before a seek / pause, so jumps
/// never click.</item>
/// <item>Fade-out window: a linear fade over song frames [start, start + length), silent after; driven by the source
/// frame number, so it is exact whatever the read size.</item>
/// </list>
/// Not thread-safe: only the audio thread (or a test) calls it. No allocations.
/// </summary>
internal sealed class PlaybackSampleProcessor
{
    /// <summary>Volume ramp duration for a full-scale change.</summary>
    public const double VolumeRampSeconds = 0.020;

    /// <summary>Length of the click-free transition ramps around seeks, play and pause.</summary>
    public const double TransitionSeconds = 0.008;

    private readonly int _channels;
    private readonly float _volumeStep;
    private float _currentVolume = 1f;
    private float _targetVolume = 1f;

    // Transition level in frames: gain = _transitionLevel / TransitionFrames. Counting frames (not a float) keeps the
    // ramp shape exact and makes "frames until silent" a plain integer.
    private int _transitionLevel;
    private int _transitionDirection;

    private long _fadeStartFrame;
    private long _fadeLengthFrames;

    public PlaybackSampleProcessor(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _channels = channels;
        VolumeRampFrames = Math.Max(1, (int)Math.Round(sampleRate * VolumeRampSeconds));
        TransitionFrames = Math.Max(1, (int)Math.Round(sampleRate * TransitionSeconds));
        _volumeStep = 1f / VolumeRampFrames;
        _transitionLevel = TransitionFrames;
    }

    public int VolumeRampFrames { get; }

    public int TransitionFrames { get; }

    /// <summary>Volume the ramp is heading to, 0..1.</summary>
    public float TargetVolume
    {
        get => _targetVolume;
        set => _targetVolume = ClampVolume(value);
    }

    /// <summary>Volume applied to the last processed frame.</summary>
    public float CurrentVolume => _currentVolume;

    /// <summary>Transition gain that the next frame gets, 0..1.</summary>
    public float TransitionGain => (float)_transitionLevel / TransitionFrames;

    /// <summary>True when the transition ramp has reached silence (after a fade-out).</summary>
    public bool IsTransitionSilent => _transitionLevel == 0;

    /// <summary>Frames until a running fade-out reaches silence (0 when already silent).</summary>
    public int FramesUntilSilent => _transitionLevel;

    public bool HasFadeOut => _fadeLengthFrames > 0;

    public long FadeStartFrame => _fadeStartFrame;

    public long FadeLengthFrames => _fadeLengthFrames;

    public static float ClampVolume(float value) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);

    /// <summary>Jumps the volume to the target (used while silent, so no ramp is needed).</summary>
    public void SnapVolume() => _currentVolume = _targetVolume;

    /// <summary>Fade from silence: frame k after this call gets gain k / <see cref="TransitionFrames"/>.</summary>
    public void BeginFadeIn()
    {
        _transitionLevel = 0;
        _transitionDirection = 1;
    }

    /// <summary>Jumps the transition gain to silence.</summary>
    public void SilenceTransition()
    {
        _transitionLevel = 0;
        _transitionDirection = 0;
    }

    /// <summary>Fade to silence from the current transition level (a running fade-in reverses smoothly).</summary>
    public void BeginFadeOut() => _transitionDirection = -1;

    /// <summary>
    /// Song fade-out over [startFrame, startFrame + lengthFrames); a length of 0 or less clears it. The start may be
    /// negative: the fade then began before the song start, so song frame 0 already gets the partly faded gain.
    /// </summary>
    public void SetFadeOut(long startFrame, long lengthFrames)
    {
        if (lengthFrames <= 0)
        {
            _fadeStartFrame = 0;
            _fadeLengthFrames = 0;
            return;
        }

        _fadeStartFrame = startFrame;
        _fadeLengthFrames = lengthFrames;
    }

    /// <summary>Gain of the fade-out window at song frame <paramref name="frame"/>: 1 before, linear inside, 0 after.</summary>
    public float FadeWindowGain(long frame)
    {
        if (_fadeLengthFrames <= 0)
        {
            return 1f;
        }

        long offset = frame - _fadeStartFrame;
        if (offset <= 0)
        {
            return 1f;
        }

        if (offset >= _fadeLengthFrames)
        {
            return 0f;
        }

        return (float)(1.0 - ((double)offset / _fadeLengthFrames));
    }

    /// <summary>
    /// Applies all gains to <paramref name="samples"/> (interleaved, whole frames). <paramref name="firstFrame"/> is the
    /// song frame of the first sample frame.
    /// </summary>
    public void Process(Span<float> samples, long firstFrame)
    {
        int frames = samples.Length / _channels;
        float target = _targetVolume;
        int index = 0;
        for (int f = 0; f < frames; f++)
        {
            float volume = _currentVolume;
            if (volume != target)
            {
                float delta = target - volume;
                volume = delta > _volumeStep ? volume + _volumeStep : delta < -_volumeStep ? volume - _volumeStep : target;
                _currentVolume = volume;
            }

            float gain = volume * ((float)_transitionLevel / TransitionFrames) * FadeWindowGain(firstFrame + f);
            AdvanceTransition();

            for (int c = 0; c < _channels; c++)
            {
                samples[index] *= gain;
                index++;
            }
        }
    }

    private void AdvanceTransition()
    {
        if (_transitionDirection == 0)
        {
            return;
        }

        _transitionLevel += _transitionDirection;
        if (_transitionLevel >= TransitionFrames)
        {
            _transitionLevel = TransitionFrames;
            _transitionDirection = 0;
        }
        else if (_transitionLevel <= 0)
        {
            _transitionLevel = 0;
            _transitionDirection = 0;
        }
    }
}
