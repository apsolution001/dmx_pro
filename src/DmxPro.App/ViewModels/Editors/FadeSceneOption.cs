namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One row of a fade editor scene list: a scene (with its colour swatch and light count), "Current look" (From only,
/// <see cref="Guid.Empty"/>) or a scene that was deleted but is still referenced.
/// </summary>
public sealed class FadeSceneOption
{
    public FadeSceneOption(Guid id, string name, string detail, string? swatch, FadeSceneKind kind)
    {
        Id = id;
        Name = name;
        Detail = detail;
        Swatch = swatch;
        Kind = kind;
    }

    /// <summary>The scene id; <see cref="Guid.Empty"/> for "Current look".</summary>
    public Guid Id { get; }

    public string Name { get; }

    /// <summary>"4 lights", "no lights yet", "what the lights show now", "deleted".</summary>
    public string Detail { get; }

    /// <summary>Approximate colour of the scene as "#RRGGBB" (a light colour); null when it has none.</summary>
    public string? Swatch { get; }

    public FadeSceneKind Kind { get; }

    public bool IsCurrentLook => Kind == FadeSceneKind.CurrentLook;

    public bool IsMissing => Kind == FadeSceneKind.Missing;

    public bool HasSwatch => Swatch is not null;

    public string AutomationName => $"{Name}, {Detail}";

    public override string ToString() => Name;
}
