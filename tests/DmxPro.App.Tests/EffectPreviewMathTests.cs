using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;

namespace DmxPro.App.Tests;

/// <summary>On-screen preview curves of the pulse and strobe editors, and the shared plain-language numbers.</summary>
public sealed class EffectPreviewMathTests
{
    [Fact]
    public void PulseLevel_Sine_StartsAtMinimumAndPeaksHalfway()
    {
        Assert.Equal(0.2, EffectPreviewMath.PulseLevel(PulseShape.Sine, 0, 0.2, 0.8), 6);
        Assert.Equal(0.8, EffectPreviewMath.PulseLevel(PulseShape.Sine, 0.5, 0.2, 0.8), 6);
        Assert.Equal(0.5, EffectPreviewMath.PulseLevel(PulseShape.Sine, 0.25, 0.2, 0.8), 6);
    }

    [Fact]
    public void PulseLevel_Triangle_IsLinear()
    {
        Assert.Equal(0.5, EffectPreviewMath.PulseLevel(PulseShape.Triangle, 0.25, 0, 1), 6);
        Assert.Equal(1.0, EffectPreviewMath.PulseLevel(PulseShape.Triangle, 0.5, 0, 1), 6);
        Assert.Equal(0.5, EffectPreviewMath.PulseLevel(PulseShape.Triangle, 0.75, 0, 1), 6);
    }

    [Fact]
    public void PulseLevel_PhaseAboveOne_Wraps()
    {
        Assert.Equal(
            EffectPreviewMath.PulseLevel(PulseShape.Sine, 0.3, 0, 1),
            EffectPreviewMath.PulseLevel(PulseShape.Sine, 1.3, 0, 1),
            6);
    }

    [Fact]
    public void PulseCurve_ShowsTwoBreathsFromMinimum()
    {
        var curve = EffectPreviewMath.PulseCurve(PulseShape.Sine, 0.1, 0.9);

        Assert.Equal(EffectPreviewMath.PulseSamples, curve.Count);
        Assert.Equal(0, curve[0].X);
        Assert.Equal(1, curve[^1].X);
        Assert.Equal(0.1, curve[0].Level, 6);
        Assert.Equal(0.1, EffectPreviewMath.LevelAt(curve, 0.5), 2);
        Assert.Equal(0.9, EffectPreviewMath.LevelAt(curve, 0.25), 2);
        Assert.Equal(0.9, EffectPreviewMath.PeakLevel(curve), 2);
    }

    [Theory]
    [InlineData(0.1, false)]
    [InlineData(0.49, false)]
    [InlineData(0.5, true)]
    [InlineData(2, true)]
    [InlineData(double.NaN, false)]
    public void CanAnimatePulse_OnlyForBreathsOfHalfASecondOrLonger(double period, bool expected) =>
        Assert.Equal(expected, EffectPreviewMath.CanAnimatePulse(period));

    [Fact]
    public void EffectiveStrobeRate_NotAllowed_IsZero() =>
        Assert.Equal(0, EffectPreviewMath.EffectiveStrobeRate(5, allowStrobe: false, maxRateHz: 10));

    [Fact]
    public void EffectiveStrobeRate_AboveSettingsMaximum_IsCapped() =>
        Assert.Equal(10, EffectPreviewMath.EffectiveStrobeRate(20, allowStrobe: true, maxRateHz: 10));

    [Fact]
    public void EffectiveStrobeRate_BelowMaximum_IsUnchanged() =>
        Assert.Equal(5, EffectPreviewMath.EffectiveStrobeRate(5, allowStrobe: true, maxRateHz: 10));

    [Fact]
    public void StrobeCurve_ZeroRate_IsSteadyFullLight()
    {
        var curve = EffectPreviewMath.StrobeCurve(0, 0.5);

        Assert.All(curve, p => Assert.Equal(1, p.Level));
        Assert.Equal(0, curve[0].X);
        Assert.Equal(1, curve[^1].X);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    [InlineData(2.5, 3)]
    public void StrobeCurve_HasOneFlashPerPeriodInOneSecond(double rate, int flashes)
    {
        var curve = EffectPreviewMath.StrobeCurve(rate, 0.5);

        var rises = 0;
        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i - 1].Level == 0 && curve[i].Level == 1)
            {
                rises++;
            }
        }

        Assert.Equal(flashes, rises);
        Assert.True(curve[^1].X <= 1);
    }

    [Fact]
    public void StrobeCurve_DutyCycle_SetsOnTimeOfEachFlash()
    {
        var curve = EffectPreviewMath.StrobeCurve(2, 0.2);

        // First flash: on from 0 to 0.1 (20 % of half a second), then off until 0.5.
        Assert.Equal(1, EffectPreviewMath.LevelAt(curve, 0.05), 6);
        Assert.Equal(0, EffectPreviewMath.LevelAt(curve, 0.3), 6);
        Assert.Equal(1, EffectPreviewMath.LevelAt(curve, 0.55), 6);
    }

    [Fact]
    public void LevelAt_InterpolatesBetweenPoints()
    {
        EffectPreviewPoint[] curve = [new(0, 0), new(1, 1)];

        Assert.Equal(0.25, EffectPreviewMath.LevelAt(curve, 0.25), 6);
        Assert.Equal(0, EffectPreviewMath.LevelAt(curve, -1), 6);
        Assert.Equal(1, EffectPreviewMath.LevelAt(curve, 2), 6);
    }

    [Fact]
    public void LevelAt_EmptyCurve_IsZero() => Assert.Equal(0, EffectPreviewMath.LevelAt([], 0.5));

    [Theory]
    [InlineData(2, "30 times a minute")]
    [InlineData(60, "once a minute")]
    [InlineData(120, "less than once a minute")]
    public void TimesPerMinute_PlainWords(double period, string expected) =>
        Assert.Equal(expected, EffectEditorFormat.TimesPerMinute(period));

    [Fact]
    public void FlashesPerSecond_SingularAndPlural()
    {
        Assert.Equal("1 flash per second", EffectEditorFormat.FlashesPerSecond(1));
        Assert.Equal("5 flashes per second", EffectEditorFormat.FlashesPerSecond(5));
    }
}
