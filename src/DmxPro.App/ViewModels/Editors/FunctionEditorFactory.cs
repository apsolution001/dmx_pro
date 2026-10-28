using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>Opens the matching editor for any function type (used by the Functions and Shows pages).</summary>
public static class FunctionEditorFactory
{
    public static FunctionEditorViewModel Create(Project project, LightFunction function, AppSettingsService settings, LightClipboardService clipboard) => function switch
    {
        SceneFunction scene => new SceneEditorViewModel(project, scene, clipboard),
        ChaserFunction chaser => new ChaserEditorViewModel(project, chaser, settings, clipboard),
        SequenceFunction sequence => new ChaserEditorViewModel(project, ConvertSequence(project, sequence), settings, clipboard),
        ColorCycleFunction cycle => new ColorCycleEditorViewModel(project, cycle),
        FadeFunction fade => new FadeEditorViewModel(project, fade),
        PulseFunction pulse => new PulseEditorViewModel(project, pulse),
        FlashFunction flash => new FlashEditorViewModel(project, flash),
        StrobeFunction strobe => new StrobeEditorViewModel(project, strobe, settings),
        _ => throw new ArgumentOutOfRangeException(nameof(function), function.Type, "No editor for this function type."),
    };

    /// <summary>
    /// Sequences are merged into chasers (owner decision 2026-10-27) and converted when a project loads; a sequence that
    /// still reaches an editor is converted here the same way: a chaser with the same id, name, owner and playback, each
    /// step an own-look scene (hidden, owned by the chaser, no scene fades) with the step's values and timing. The
    /// sequence is replaced in place, so everything that points at its id keeps working.
    /// </summary>
    private static ChaserFunction ConvertSequence(Project project, SequenceFunction sequence)
    {
        var chaser = new ChaserFunction
        {
            Id = sequence.Id,
            Name = sequence.Name,
            OwnerShowId = sequence.OwnerShowId,
            OwnerFunctionId = sequence.OwnerFunctionId,
            ShowInFunctionList = sequence.ShowInFunctionList,
            CopiedFromId = sequence.CopiedFromId,
            RunOrder = sequence.RunOrder,
            Direction = sequence.Direction,
        };

        var scenes = new List<LightFunction>();
        for (var i = 0; i < sequence.Steps.Count; i++)
        {
            var step = sequence.Steps[i];
            var name = string.IsNullOrWhiteSpace(step.Name) ? $"Step {i + 1}" : step.Name;
            var scene = ChaserSteps.NewOwnLookScene(chaser.Id, name, step.Values);
            scenes.Add(scene);
            chaser.Steps.Add(new ChaserStep
            {
                FunctionId = scene.Id,
                FadeInSeconds = step.FadeInSeconds,
                HoldSeconds = step.HoldSeconds,
                FadeOutSeconds = step.FadeOutSeconds,
            });
        }

        var index = project.Functions.IndexOf(sequence);
        if (index >= 0)
        {
            project.Functions[index] = chaser;
        }
        else
        {
            project.Functions.Add(chaser);
        }

        project.Functions.AddRange(scenes);
        return chaser;
    }
}
