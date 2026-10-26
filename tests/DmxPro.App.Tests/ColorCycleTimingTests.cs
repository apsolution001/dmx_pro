using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;

namespace DmxPro.App.Tests;

/// <summary>Colour cycle editor maths: round length, spread ⇄ offset, colour at a moment, colour names, quick fills.</summary>
public sealed class ColorCycleTimingTests
{
    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor Green = new(0, 255, 0);
    private static readonly RgbColor Blue = new(0, 0, 255);

    [Theory]
    [InlineData(3, 1, 1, 6)]
    [InlineData(1, 2, 0, 2)]
    [InlineData(0, 1, 1, 0)]
    [InlineData(2, 0, 0, 0.1)] // each slot stretched to the engine's shortest step (0.05 s)
    public void RoundSeconds_CountHoldFade_IsCountTimesSlot(int count, double hold, double fade, double expected)
    {
        Assert.Equal(expected, ColorCycleTiming.RoundSeconds(count, hold, fade), 6);
    }

    [Fact]
    public void OffsetForSpread_FullSpread_IsRoundDividedByLights()
    {
        // 3 colours × 2 s = 6 s round, 4 lights: 100 % = 1.5 s between lights.
        Assert.Equal(1.5, ColorCycleTiming.OffsetForSpread(100, 6, 4), 6);
        Assert.Equal(0.75, ColorCycleTiming.OffsetForSpread(50, 6, 4), 6);
    }

    [Theory]
    [InlineData(0, 6, 4)]
    [InlineData(-10, 6, 4)]
    [InlineData(50, 6, 1)]
    [InlineData(50, 0, 4)]
    [InlineData(double.NaN, 6, 4)]
    public void OffsetForSpread_NothingToSpread_IsZero(double percent, double round, int lights)
    {
        Assert.Equal(0, ColorCycleTiming.OffsetForSpread(percent, round, lights));
    }

    [Fact]
    public void OffsetForSpread_RoundsToMilliseconds()
    {
        Assert.Equal(0.667, ColorCycleTiming.OffsetForSpread(100, 2, 3));
    }

    [Fact]
    public void SpreadForOffset_IsInverseOfOffsetForSpread()
    {
        Assert.Equal(50, ColorCycleTiming.SpreadForOffset(0.75, 6, 4), 6);
        Assert.Equal(200, ColorCycleTiming.SpreadForOffset(3, 6, 4), 6);
        Assert.Equal(0, ColorCycleTiming.SpreadForOffset(1, 6, 1));
        Assert.Equal(0, ColorCycleTiming.SpreadForOffset(1, 0, 4));
    }

    [Fact]
    public void ColorAt_DuringHold_IsTheColour()
    {
        RgbColor[] colors = [Red, Green, Blue];

        Assert.Equal(Red, ColorCycleTiming.ColorAt(colors, 1, 1, 0));
        Assert.Equal(Red, ColorCycleTiming.ColorAt(colors, 1, 1, 0.99));
        Assert.Equal(Green, ColorCycleTiming.ColorAt(colors, 1, 1, 2.5));
    }

    [Fact]
    public void ColorAt_HalfwayThroughFade_IsHalfMix()
    {
        RgbColor[] colors = [Red, Green, Blue];

        Assert.Equal(new RgbColor(128, 128, 0), ColorCycleTiming.ColorAt(colors, 1, 1, 1.5));
    }

    [Fact]
    public void ColorAt_LastColourFadesBackToFirst()
    {
        RgbColor[] colors = [Red, Green, Blue];

        Assert.Equal(new RgbColor(128, 0, 128), ColorCycleTiming.ColorAt(colors, 1, 1, 5.5));
    }

    [Fact]
    public void ColorAt_NegativeTime_WrapsToEndOfRound()
    {
        RgbColor[] colors = [Red, Green, Blue];

        // -2 s = 4 s into the 6 s round: blue holding.
        Assert.Equal(Blue, ColorCycleTiming.ColorAt(colors, 1, 1, -2));
    }

    [Fact]
    public void ColorAt_NoColours_IsBlack()
    {
        Assert.Equal(default, ColorCycleTiming.ColorAt([], 1, 1, 3));
    }

    [Fact]
    public void LightColorsAtStart_FullSpread_SpreadsColoursEvenly()
    {
        RgbColor[] colors = [Red, Green, Blue];

        // 3 lights, offset = round / 3 = 2 s: light 0 at 0 s (red), light 1 at -2 s (blue), light 2 at -4 s (green).
        var lights = ColorCycleTiming.LightColorsAtStart(colors, 1, 1, 2, 3);

        Assert.Equal([Red, Blue, Green], lights);
    }

    [Fact]
    public void LightColorsAtStart_NoSpread_AllFirstColour()
    {
        var lights = ColorCycleTiming.LightColorsAtStart([Red, Green], 1, 1, 0, 4);

        Assert.All(lights, c => Assert.Equal(Red, c));
    }

    [Theory]
    [InlineData("#FF0000", "Red")]
    [InlineData("#FFD8A0", "Warm white")]
    [InlineData("#FF8000", "Orange")]
    [InlineData("#000000", "Off (black)")]
    [InlineData("#FF7000", "Orange")]
    [InlineData("#000060", "Dark blue")]
    [InlineData("#F0F0F0", "White")]
    [InlineData("#808080", "Grey")]
    [InlineData("#FFA0D0", "Light pink")]
    [InlineData("#20E040", "Green")]
    public void NameOf_Colour_GivesPlainName(string hex, string expected)
    {
        Assert.True(RgbColor.TryParseHex(hex, out var color));

        Assert.Equal(expected, ColorCycleColorNames.NameOf(color));
    }

    [Fact]
    public void Palettes_AllColoursHaveRealNames()
    {
        Assert.Equal(["Rainbow", "Warm", "Cool"], ColorCyclePalette.All.Select(p => p.Name));
        foreach (var color in ColorCyclePalette.All.SelectMany(p => p.Colors))
        {
            Assert.DoesNotContain("Dark", ColorCycleColorNames.NameOf(color), StringComparison.Ordinal);
        }
    }
}
