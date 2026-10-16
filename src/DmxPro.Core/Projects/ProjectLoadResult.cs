using DmxPro.Core.Validation;

namespace DmxPro.Core.Projects;

/// <summary>A loaded project plus any problems found in it (shown to the user, not thrown).</summary>
public sealed record ProjectLoadResult(Project Project, IReadOnlyList<ValidationIssue> Issues);
