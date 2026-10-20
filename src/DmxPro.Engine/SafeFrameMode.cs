namespace DmxPro.Engine;

/// <summary>What the last frame before stopping looks like (safety rule 6).</summary>
public enum SafeFrameMode
{
    /// <summary>All functions released; every channel at its profile default.</summary>
    ProfileDefaults,

    /// <summary>All functions released and blackout applied (intensity channels at 0).</summary>
    Blackout,
}
