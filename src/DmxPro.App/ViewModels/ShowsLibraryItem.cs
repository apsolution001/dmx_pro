using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels;

/// <summary>A function in the Shows page library (what the Functions page lists), with a colour swatch.</summary>
public sealed class ShowsLibraryItem
{
    /// <summary>How deep chasers are followed to find a colour (guards against reference loops).</summary>
    private const int MaxPreviewDepth = 4;

    public ShowsLibraryItem(LightFunction function, Project project)
    {
        Function = function;
        var color = PreviewColor(function, project);
        ColorHex = color?.ToHex();
    }

    public LightFunction Function { get; }

    public Guid Id => Function.Id;

    public string Name => Function.Name;

    public string TypeName => TypeLabel(Function.Type);

    /// <summary>"#RRGGBB" of the light colour the function shows, or null when unknown (the view uses the accent).</summary>
    public string? ColorHex { get; }

    public bool HasColor => ColorHex is not null;

    /// <summary>User-facing name of a function type.</summary>
    public static string TypeLabel(FunctionType type) => FunctionRowViewModel.TypeDisplayName(type);

    /// <summary>
    /// Approximate light colour of a function, for swatches only: a scene's estimated colour, a sequence's first step
    /// and a chaser's first step that has a colour. Null for anything else.
    /// </summary>
    public static RgbColor? PreviewColor(LightFunction? function, Project project) => PreviewColor(function, project, 0);

    private static RgbColor? PreviewColor(LightFunction? function, Project project, int depth) => function switch
    {
        SceneFunction scene => ScenePreviewColor.Estimate(scene, project),
        SequenceFunction sequence => sequence.Steps.Count > 0 ? ScenePreviewColor.Estimate(sequence.Steps[0].AsScene(), project) : null,
        ChaserFunction chaser when depth < MaxPreviewDepth => chaser.Steps
            .Select(s => PreviewColor(project.FindFunction(s.FunctionId), project, depth + 1))
            .FirstOrDefault(c => c is not null),
        _ => null,
    };
}
