using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>Fade editor: from-scene and to-scene (scenes only) and the duration.</summary>
public sealed partial class FadeEditorViewModel : FunctionEditorViewModel
{
    private double _durationSeconds;

    /// <summary>Chosen start scene; <see cref="Guid.Empty"/> = from whatever is playing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText), nameof(HasNoTarget))]
    private Guid _fromSceneId;

    /// <summary>Chosen target scene; <see cref="Guid.Empty"/> = not chosen yet (the fade shows nothing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText), nameof(HasNoTarget))]
    private Guid _toSceneId;

    public FadeEditorViewModel(Project project, FadeFunction fade)
        : base(project, fade)
    {
        var scenes = project.Functions
            .OfType<SceneFunction>()
            .Where(s => s.ShowInFunctionList || s.OwnerShowId is null || s.Id == fade.FromSceneId || s.Id == fade.ToSceneId)
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new EditorOption<Guid>(s.Id, s.Name, s.Name))
            .ToList();

        FromOptions = [new(Guid.Empty, "(whatever is playing)", "Start from the current light state"), .. scenes];
        ToOptions = [new(Guid.Empty, "(choose a scene)", "The fade shows nothing until a target scene is chosen"), .. scenes];

        // A scene that was deleted stays selectable so the user sees (and can change) the broken reference.
        foreach (var id in new[] { fade.FromSceneId, fade.ToSceneId })
        {
            if (id is { } missing && scenes.All(s => s.Value != missing))
            {
                var option = new EditorOption<Guid>(missing, "(missing scene)", "This scene no longer exists — pick another one");
                FromOptions.Add(option);
                ToOptions.Add(option);
            }
        }

        LoadFromOriginal();
    }

    public List<EditorOption<Guid>> FromOptions { get; }

    public List<EditorOption<Guid>> ToOptions { get; }

    public bool HasNoScenes => FromOptions.Count <= 1;

    public bool HasNoTarget => ToSceneId == Guid.Empty;

    public double? DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            SetNumber(ref _durationSeconds, value, 0, FunctionEditing.MaxSeconds);
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    /// <summary>"Red → Blue over 3 s".</summary>
    public string SummaryText =>
        $"{NameOf(FromOptions, FromSceneId)} → {NameOf(ToOptions, ToSceneId)} over {EditorTiming.FormatDuration(_durationSeconds)}, then holds the target";

    private FadeFunction Fade => (FadeFunction)Original;

    protected override void LoadDraft()
    {
        FromSceneId = Fade.FromSceneId ?? Guid.Empty;
        ToSceneId = Fade.ToSceneId ?? Guid.Empty;
        DurationSeconds = Fade.DurationSeconds;
    }

    protected override void ApplyDraft()
    {
        Fade.FromSceneId = FromSceneId == Guid.Empty ? null : FromSceneId;
        Fade.ToSceneId = ToSceneId == Guid.Empty ? null : ToSceneId;
        Fade.DurationSeconds = _durationSeconds;
    }

    partial void OnFromSceneIdChanged(Guid value) => MarkDirty();

    partial void OnToSceneIdChanged(Guid value) => MarkDirty();

    private static string NameOf(List<EditorOption<Guid>> options, Guid id) =>
        options.FirstOrDefault(o => o.Value == id)?.Label ?? "(missing scene)";
}
