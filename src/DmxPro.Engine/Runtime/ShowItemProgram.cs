namespace DmxPro.Engine.Runtime;

/// <summary>One show item: a function active between <see cref="Start"/> and <see cref="End"/> with fade wedges.</summary>
internal sealed class ShowItemProgram
{
    public ShowItemProgram(FunctionProgram function, double start, double end, double fadeIn, double fadeOut)
    {
        Function = function;
        Start = start;
        End = end;
        FadeIn = fadeIn;
        FadeOut = fadeOut;
    }

    public FunctionProgram Function { get; }

    public double Start { get; }

    public double End { get; }

    public double FadeIn { get; }

    public double FadeOut { get; }

    /// <summary>Fade level at show position <paramref name="position"/>; 0 outside [Start, End).</summary>
    public float LevelAt(double position)
    {
        if (position < Start || position >= End)
        {
            return 0f;
        }

        var level = 1.0;
        if (FadeIn > 0 && position - Start < FadeIn)
        {
            level = (position - Start) / FadeIn;
        }

        if (FadeOut > 0 && End - position < FadeOut)
        {
            level = Math.Min(level, (End - position) / FadeOut);
        }

        return (float)level;
    }
}
