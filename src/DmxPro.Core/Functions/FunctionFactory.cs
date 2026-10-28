namespace DmxPro.Core.Functions;

/// <summary>Creates empty functions of any <see cref="FunctionType"/> with their default settings.</summary>
public static class FunctionFactory
{
    /// <summary>
    /// A new, empty function of <paramref name="type"/> (unnamed, library-owned). <see cref="FunctionType.Sequence"/>
    /// creates a <see cref="ChaserFunction"/>: Sequence was merged into Chaser, the enum value only remains so old files
    /// can still be read.
    /// </summary>
    public static LightFunction Create(FunctionType type) => type switch
    {
        FunctionType.Scene => new SceneFunction(),
        FunctionType.Chaser => new ChaserFunction(),
        FunctionType.Sequence => new ChaserFunction(),
        FunctionType.ColorCycle => new ColorCycleFunction(),
        FunctionType.Fade => new FadeFunction(),
        FunctionType.Pulse => new PulseFunction(),
        FunctionType.Flash => new FlashFunction(),
        FunctionType.Strobe => new StrobeFunction(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown function type."),
    };

    /// <summary>Base of default names ("Chaser" → "Chaser 1", "Chaser 2", …).</summary>
    public static string DefaultBaseName(FunctionType type) => type switch
    {
        FunctionType.ColorCycle => "Color cycle",
        FunctionType.Sequence => "Chaser",
        _ => type.ToString(),
    };
}
