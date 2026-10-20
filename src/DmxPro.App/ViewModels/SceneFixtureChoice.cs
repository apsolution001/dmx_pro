using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// A tickable fixture or group in the scene editor's General tab and in the Add flyout of the sequence and effect
/// editors. Ticking adds the fixture (or every member of the group) to the function; unticking removes it.
/// <see cref="SetInSceneSilently"/> updates the tick without calling back.
/// </summary>
public sealed partial class SceneFixtureChoice : ObservableObject
{
    private readonly Action<SceneFixtureChoice, bool> _onToggled;
    private bool _silent;

    [ObservableProperty]
    private bool _isInScene;

    public SceneFixtureChoice(Guid id, string name, string detail, bool isGroup, IReadOnlyList<Guid> fixtureIds, Action<SceneFixtureChoice, bool> onToggled)
    {
        Id = id;
        Name = name;
        Detail = detail;
        IsGroup = isGroup;
        FixtureIds = fixtureIds;
        _onToggled = onToggled;
    }

    /// <summary>Fixture id, or group id for a group.</summary>
    public Guid Id { get; }

    public string Name { get; }

    /// <summary>"U1 · 1–8 · Generic RGBW" for a fixture, member count for a group.</summary>
    public string Detail { get; }

    public bool IsGroup { get; }

    /// <summary>The fixtures this choice stands for (just itself for a fixture).</summary>
    public IReadOnlyList<Guid> FixtureIds { get; }

    public string IconKind => IsGroup ? "Group" : "Fixtures";

    public string AutomationName => IsGroup ? $"Use group {Name}" : $"Use fixture {Name}";

    public void SetInSceneSilently(bool value)
    {
        _silent = true;
        IsInScene = value;
        _silent = false;
    }

    partial void OnIsInSceneChanged(bool value)
    {
        if (!_silent)
        {
            _onToggled(this, value);
        }
    }
}
