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
    [ObservableProperty]
    private bool _useWhiteChannel;

    protected EffectEditorViewModel(Project project, FixtureEffectFunction effect)
        : base(project, effect)
    {
        Fixtures = new FixtureListEditorViewModel(project, OnFixturesChanged);
    }

    public FixtureListEditorViewModel Fixtures { get; }

    /// <summary>The effect has no lights yet: the settings panel says so and offers "Choose lights".</summary>
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

    partial void OnUseWhiteChannelChanged(bool value) => MarkDirty();

    private void OnFixturesChanged(IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed)
    {
        OnPropertyChanged(nameof(HasNoFixtures));
        MarkDirty();
    }
}
