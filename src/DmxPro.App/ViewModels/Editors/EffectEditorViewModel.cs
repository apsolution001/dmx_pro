using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Base of the editors of fixture effects (color cycle, pulse, flash, strobe): the ordered fixture list and "use the
/// white channel". The effect's own settings live in the derived editor's properties (the draft) until Save.
/// </summary>
public abstract partial class EffectEditorViewModel : FunctionEditorViewModel
{
    /// <summary>Index of the "Lights" tab in editors that use tabs (<see cref="SelectedTabIndex"/>).</summary>
    public const int LightsTabIndex = 0;

    /// <summary>Index of the effect's settings tab in editors that use tabs.</summary>
    public const int SettingsTabIndex = 1;

    [ObservableProperty]
    private bool _useWhiteChannel;

    /// <summary>Selected tab of editors with a Lights tab and a settings tab; not part of the draft.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    protected EffectEditorViewModel(Project project, FixtureEffectFunction effect)
        : base(project, effect)
    {
        Fixtures = new FixtureListEditorViewModel(project, OnFixturesChanged);
    }

    public FixtureListEditorViewModel Fixtures { get; }

    /// <summary>The effect has no lights yet: the settings tab says so and offers "Choose lights".</summary>
    public bool HasNoFixtures => Fixtures.HasNoItems;

    public override void RefreshPatch() => Fixtures.RefreshPatch();

    private FixtureEffectFunction Effect => (FixtureEffectFunction)Target;

    protected sealed override void LoadDraft()
    {
        Fixtures.Load(Effect.FixtureIds);
        OnPropertyChanged(nameof(HasNoFixtures));
        UseWhiteChannel = Effect.UseWhiteChannel;
        LoadEffect();
    }

    protected sealed override void ApplyDraft()
    {
        Effect.FixtureIds = [.. Fixtures.FixtureIds];
        Effect.UseWhiteChannel = UseWhiteChannel;
        ApplyEffect();
    }

    /// <summary>Loads the effect's own settings into the editor properties.</summary>
    protected abstract void LoadEffect();

    /// <summary>Writes the editor properties into the effect.</summary>
    protected abstract void ApplyEffect();

    /// <summary>Opens the settings tab when the effect already has lights, else the Lights tab (like the scene editor).</summary>
    protected void SelectStartTab() => SelectedTabIndex = Fixtures.HasNoItems ? LightsTabIndex : SettingsTabIndex;

    partial void OnUseWhiteChannelChanged(bool value) => MarkDirty();

    private void OnFixturesChanged(IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed)
    {
        OnPropertyChanged(nameof(HasNoFixtures));
        MarkDirty();
    }
}
