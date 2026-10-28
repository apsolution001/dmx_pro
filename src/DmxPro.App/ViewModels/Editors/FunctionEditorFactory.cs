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
        SequenceFunction sequence => new ChaserEditorViewModel(project, SequenceMigration.ConvertInPlace(project, sequence), settings, clipboard),
        ColorCycleFunction cycle => new ColorCycleEditorViewModel(project, cycle),
        FadeFunction fade => new FadeEditorViewModel(project, fade),
        PulseFunction pulse => new PulseEditorViewModel(project, pulse),
        FlashFunction flash => new FlashEditorViewModel(project, flash),
        StrobeFunction strobe => new StrobeEditorViewModel(project, strobe, settings),
        _ => throw new ArgumentOutOfRangeException(nameof(function), function.Type, "No editor for this function type."),
    };
}
