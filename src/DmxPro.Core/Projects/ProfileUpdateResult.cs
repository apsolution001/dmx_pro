using DmxPro.Core.Validation;

namespace DmxPro.Core.Projects;

/// <summary>What <see cref="Project.UpdateProfile"/> changed and what is now wrong because of it.</summary>
public sealed class ProfileUpdateResult
{
    /// <summary>Fixtures that use the updated profile (their channel layout changed with it).</summary>
    public IReadOnlyList<Guid> AffectedFixtureIds { get; init; } = [];

    /// <summary>
    /// Patch problems of the affected fixtures after the update: footprints that now overlap another fixture or run past
    /// address 512, missing modes. The "profile unverified" warning is left out (user layouts are always unverified).
    /// </summary>
    public IReadOnlyList<ValidationIssue> PatchIssues { get; init; } = [];

    /// <summary>
    /// Scenes/sequences that still hold values for channels the new layout no longer has
    /// (<c>SCENE_UNKNOWN_CHANNEL</c> / <c>SEQUENCE_UNKNOWN_CHANNEL</c>). Playback ignores those values.
    /// </summary>
    public IReadOnlyList<ValidationIssue> FunctionIssues { get; init; } = [];
}
