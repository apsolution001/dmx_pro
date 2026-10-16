namespace DmxPro.Core.Validation;

/// <summary>
/// A user-facing problem found while validating profiles, patching, or project files.
/// </summary>
/// <param name="Severity">Errors block the action (e.g. enabling output); warnings are shown but allowed.</param>
/// <param name="Code">Stable machine-readable code, e.g. <c>ADDRESS_OVERLAP</c>.</param>
/// <param name="Message">Plain-language explanation for the user.</param>
/// <param name="EntityId">The fixture/group/function the issue belongs to, if any.</param>
public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message, Guid? EntityId = null)
{
    public static ValidationIssue Error(string code, string message, Guid? entityId = null) =>
        new(ValidationSeverity.Error, code, message, entityId);

    public static ValidationIssue Warning(string code, string message, Guid? entityId = null) =>
        new(ValidationSeverity.Warning, code, message, entityId);
}
