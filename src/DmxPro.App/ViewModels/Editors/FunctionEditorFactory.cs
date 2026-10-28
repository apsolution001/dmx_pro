using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>Opens the matching editor for a function (used by the Functions and Shows pages and the chaser's step editor).</summary>
public static class FunctionEditorFactory
{
    /// <summary>
    /// A scene or chaser editor (an old sequence is turned into a chaser first). Any other type is a legacy function the
    /// app no longer edits: it gets <see cref="UnsupportedFunctionEditorViewModel"/>, a short note instead of a crash.
    /// </summary>
    public static FunctionEditorViewModel Create(Project project, LightFunction function, AppSettingsService settings, LightClipboardService clipboard) => function switch
    {
        SceneFunction scene => new SceneEditorViewModel(project, scene, clipboard),
        ChaserFunction chaser => new ChaserEditorViewModel(project, chaser, settings, clipboard),
        SequenceFunction sequence => new ChaserEditorViewModel(project, SequenceMigration.ConvertInPlace(project, sequence), settings, clipboard),
        _ => new UnsupportedFunctionEditorViewModel(project, function),
    };
}
