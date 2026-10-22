using DmxPro.Core.Functions;

namespace DmxPro.Engine;

/// <summary>
/// What happens to the live edit preview of an editor draft when the application loses focus (safety rule 5:
/// momentary actions release on focus loss). A flash draft previews as held, i.e. it behaves like a held flash button,
/// so it is released when the app is deactivated. Every other draft is a lasting look and stays on the lights.
/// </summary>
public static class PreviewFocusPolicy
{
    /// <summary>True when the preview of a draft of <paramref name="type"/> must be released on application focus loss.</summary>
    /// <param name="type">Type of the previewed draft.</param>
    /// <returns>True for <see cref="FunctionType.Flash"/>.</returns>
    public static bool ReleasesOnFocusLoss(FunctionType type) => type == FunctionType.Flash;
}
