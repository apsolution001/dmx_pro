using DmxPro.Core.Validation;

namespace DmxPro.Core.Projects;

/// <summary>A loaded project plus any problems found in it (shown to the user, not thrown).</summary>
public sealed record ProjectLoadResult(Project Project, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>
    /// <see cref="ProjectSerializer.ComputeContentHash"/> of the file's bytes when it was read from a file
    /// (<see cref="ProjectSerializer.LoadAsync"/>); null for a project parsed from a string.
    /// </summary>
    public string? ContentHash { get; init; }
}
