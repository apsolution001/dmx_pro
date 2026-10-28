namespace DmxPro.App.ViewModels.Editors;

/// <summary>What a chaser step plays, as the chaser editor shows it (not stored: worked out from the step's function).</summary>
public enum ChaserStepKind
{
    /// <summary>An own look: a scene the chaser owns, edited right in the chaser.</summary>
    OwnLook,

    /// <summary>The chaser's own copy of another function (a chaser), edited with "Edit step".</summary>
    FunctionStep,

    /// <summary>An older step that still plays a function from the Functions list; its first edit makes an own copy.</summary>
    Linked,

    /// <summary>The step's function was deleted; the step plays nothing.</summary>
    Missing,
}
