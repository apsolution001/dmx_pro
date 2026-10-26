namespace DmxPro.App.ViewModels.Editors;

/// <summary>What a <see cref="FadeSceneOption"/> stands for.</summary>
public enum FadeSceneKind
{
    /// <summary>A scene of the project.</summary>
    Scene,

    /// <summary>From only: start from whatever the lights show now (the fade has no From scene).</summary>
    CurrentLook,

    /// <summary>The fade points at a scene that no longer exists.</summary>
    Missing,
}
