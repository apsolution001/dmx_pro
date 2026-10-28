namespace DmxPro.Core.Functions;

/// <summary>Creates empty functions of any <see cref="FunctionType"/> with their default settings.</summary>
public static class FunctionFactory
{
    /// <summary>
    /// A new, empty function of <paramref name="type"/> (unnamed, library-owned). DMX Pro offers only Scene and Chaser
    /// (owner decision 2026-10-28): every other type (Sequence, and the retired colour cycle, fade, pulse, flash and
    /// strobe) creates a <see cref="ChaserFunction"/>. Those enum values only remain so old files can still be read.
    /// </summary>
    public static LightFunction Create(FunctionType type) => type switch
    {
        FunctionType.Scene => new SceneFunction(),
        FunctionType.Chaser => new ChaserFunction(),
        FunctionType.Sequence => new ChaserFunction(),
        FunctionType.ColorCycle => new ChaserFunction(),
        FunctionType.Fade => new ChaserFunction(),
        FunctionType.Pulse => new ChaserFunction(),
        FunctionType.Flash => new ChaserFunction(),
        FunctionType.Strobe => new ChaserFunction(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown function type."),
    };

    /// <summary>
    /// Base of default names ("Chaser" → "Chaser 1", "Chaser 2", …): "Scene" for scenes, "Chaser" for every type that
    /// creates a chaser.
    /// </summary>
    public static string DefaultBaseName(FunctionType type) => type switch
    {
        FunctionType.Scene => "Scene",
        _ => "Chaser",
    };
}
