using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Fade editor: a "From" and a "To" panel (each the chosen scene with its lights as colour dots and a searchable scene
/// list), a swap button, the duration with quick presets, a plain-words explanation, an on-screen colour strip and
/// inline notes when scenes are missing or use different lights. The texts describe what the engine's fade does
/// (<c>FadeProgram</c>): channels in both scenes move smoothly; channels only in To fade in over whatever is underneath;
/// channels only in From fade back to what is underneath; no From scene = the To scene fades in over what is there now;
/// no To scene = the fade outputs nothing; after the duration it holds To until stopped.
/// </summary>
public sealed partial class FadeEditorViewModel : FunctionEditorViewModel
{
    /// <summary>Durations of the quick buttons, in seconds.</summary>
    public static readonly double[] PresetSeconds = [1, 3, 5, 10];

    private const int MaxNamesListed = 3;

    private double _durationSeconds;

    /// <summary>Chosen start scene; <see cref="Guid.Empty"/> = Current look (whatever the lights show now).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwapCommand))]
    private Guid _fromSceneId;

    /// <summary>Chosen target scene; <see cref="Guid.Empty"/> = not chosen yet (the fade shows nothing).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwapCommand))]
    private Guid _toSceneId;

    public FadeEditorViewModel(Project project, FadeFunction fade)
        : base(project, fade)
    {
        From = new FadeSideViewModel(project, isFrom: true, id => FromSceneId = id);
        To = new FadeSideViewModel(project, isFrom: false, id => ToSceneId = id);
        DurationPresets = PresetSeconds.Select(s => new FadeDurationPreset(s, SetDuration)).ToList();
        LoadFromOriginal();
    }

    public FadeSideViewModel From { get; }

    public FadeSideViewModel To { get; }

    public IReadOnlyList<FadeDurationPreset> DurationPresets { get; }

    /// <summary>Radio group name of the quick duration buttons, unique per editor so two open fade editors never share it.</summary>
    public string PresetGroupName { get; } = $"FadeDuration-{Guid.NewGuid():N}";

    /// <summary>True when the project has no scene at all (the editor shows an empty state).</summary>
    public bool HasNoScenes { get; private set; }

    public bool HasScenes => !HasNoScenes;

    /// <summary>Same meaning as before the redesign: no To scene chosen.</summary>
    public bool HasNoTarget => ToSceneId == Guid.Empty;

    public double? DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            SetNumber(ref _durationSeconds, value, 0, FunctionEditing.MaxSeconds);
            UpdatePresets();
            UpdateDerived();
        }
    }

    /// <summary>One plain sentence: what the fade does with the current choices.</summary>
    public string SummaryText { get; private set; } = string.Empty;

    /// <summary>Inline notes and warnings (deleted scenes, different lights, same scene, empty scene).</summary>
    public IReadOnlyList<FadeNotice> Notices { get; private set; } = [];

    public bool HasNotices => Notices.Count > 0;

    /// <summary>True when the colour strip can be drawn (a To scene with a colour).</summary>
    public bool HasGradient { get; private set; }

    /// <summary>Left end of the strip as "#AARRGGBB": the From colour, or the To colour fully transparent for Current look.</summary>
    public string GradientStart { get; private set; } = "#00000000";

    /// <summary>Right end of the strip as "#AARRGGBB": the To colour.</summary>
    public string GradientEnd { get; private set; } = "#00000000";

    /// <summary>"Red" / "Now": label under the strip's left end.</summary>
    public string GradientStartLabel => From.IsCurrentLook ? "Now" : From.DisplayName;

    public string GradientEndLabel => To.DisplayName;

    /// <summary>"3 s" under the middle of the strip.</summary>
    public string DurationLabel => EditorTiming.FormatDuration(_durationSeconds);

    public string GradientAutomationName =>
        $"Colour preview: {GradientStartLabel} to {GradientEndLabel} over {DurationLabel}";

    private FadeFunction Fade => (FadeFunction)Target;

    /// <summary>Scenes were added, renamed or deleted elsewhere: rebuild both lists, keeping the choices.</summary>
    public override void RefreshPatch()
    {
        RebuildOptions();
        UpdateDerived();
    }

    /// <summary>"PAR 1, PAR 2, PAR 3 and 2 more".</summary>
    internal static string ListNames(IReadOnlyList<string> names)
    {
        if (names.Count <= MaxNamesListed)
        {
            return string.Join(", ", names);
        }

        return string.Join(", ", names.Take(MaxNamesListed)) + $" and {names.Count - MaxNamesListed} more";
    }

    protected override void LoadDraft()
    {
        FromSceneId = Fade.FromSceneId ?? Guid.Empty;
        ToSceneId = Fade.ToSceneId ?? Guid.Empty;
        DurationSeconds = Fade.DurationSeconds;
        RebuildOptions();
        UpdateDerived();
    }

    protected override void ApplyDraft()
    {
        Fade.FromSceneId = FromSceneId == Guid.Empty ? null : FromSceneId;
        Fade.ToSceneId = ToSceneId == Guid.Empty ? null : ToSceneId;
        Fade.DurationSeconds = _durationSeconds;
    }

    partial void OnFromSceneIdChanged(Guid value)
    {
        From.Show(value);
        MarkDirty();
        UpdateDerived();
    }

    partial void OnToSceneIdChanged(Guid value)
    {
        To.Show(value);
        MarkDirty();
        OnPropertyChanged(nameof(HasNoTarget));
        UpdateDerived();
    }

    private bool CanSwap() => FromSceneId != Guid.Empty && ToSceneId != Guid.Empty && FromSceneId != ToSceneId;

    /// <summary>From becomes To and To becomes From.</summary>
    [RelayCommand(CanExecute = nameof(CanSwap))]
    private void Swap() => (FromSceneId, ToSceneId) = (ToSceneId, FromSceneId);

    [RelayCommand]
    private void SetDuration(FadeDurationPreset? preset)
    {
        if (preset is not null)
        {
            DurationSeconds = preset.Seconds;
        }
    }

    private SceneFunction? FindScene(Guid id) =>
        id == Guid.Empty ? null : Project.FindFunction(id) as SceneFunction;

    private void RebuildOptions()
    {
        var scenes = Project.Functions
            .OfType<SceneFunction>()
            .Where(s => s.ShowInFunctionList || s.OwnerShowId is null || s.Id == FromSceneId || s.Id == ToSceneId)
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(SceneOption)
            .ToList();

        HasNoScenes = scenes.Count == 0;
        OnPropertyChanged(nameof(HasNoScenes));
        OnPropertyChanged(nameof(HasScenes));

        var fromOptions = new List<FadeSceneOption>(scenes.Count + 2)
        {
            new(Guid.Empty, "Current look", "what the lights show now", null, FadeSceneKind.CurrentLook),
        };
        fromOptions.AddRange(scenes);
        AddMissing(fromOptions, FromSceneId);

        var toOptions = new List<FadeSceneOption>(scenes);
        AddMissing(toOptions, ToSceneId);

        From.SetOptions(fromOptions, FromSceneId);
        To.SetOptions(toOptions, ToSceneId);
    }

    private FadeSceneOption SceneOption(SceneFunction scene)
    {
        var count = scene.TargetFixtureIds().Count(id => Project.FindFixture(id) is not null);
        var detail = count switch
        {
            0 => "no lights yet",
            1 => "1 light",
            _ => $"{count} lights",
        };
        return new FadeSceneOption(scene.Id, scene.Name, detail, ScenePreviewColor.Estimate(scene, Project)?.ToHex(), FadeSceneKind.Scene);
    }

    // A deleted scene stays in the list (and selected) so the user sees the broken reference and can change it.
    private void AddMissing(List<FadeSceneOption> options, Guid id)
    {
        if (id != Guid.Empty && options.All(o => o.Id != id))
        {
            options.Add(new FadeSceneOption(id, "(deleted scene)", "deleted", null, FadeSceneKind.Missing));
        }
    }

    private void UpdatePresets()
    {
        foreach (var preset in DurationPresets)
        {
            preset.IsActive = Math.Abs(preset.Seconds - _durationSeconds) < 0.0005;
        }
    }

    private void UpdateDerived()
    {
        var from = FindScene(FromSceneId);
        var to = FindScene(ToSceneId);

        SummaryText = Describe(from, to);
        Notices = BuildNotices(from, to);
        UpdateGradient(from, to);

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(Notices));
        OnPropertyChanged(nameof(HasNotices));
        OnPropertyChanged(nameof(HasGradient));
        OnPropertyChanged(nameof(GradientStart));
        OnPropertyChanged(nameof(GradientEnd));
        OnPropertyChanged(nameof(GradientStartLabel));
        OnPropertyChanged(nameof(GradientEndLabel));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(GradientAutomationName));
    }

    private string Describe(SceneFunction? from, SceneFunction? to)
    {
        if (to is null)
        {
            return ToSceneId == Guid.Empty
                ? "Pick a To scene: until then this fade does nothing."
                : "The To scene was deleted: this fade does nothing until you pick another one.";
        }

        var target = Quote(to.Name);
        if (_durationSeconds <= 0)
        {
            return $"Jumps straight to {target} (no fade), then stays on {target} until the fade is stopped.";
        }

        var time = EditorTiming.FormatDuration(_durationSeconds);
        return from is null
            ? $"Over {time} the lights move from what they show now to {target}, then stay on {target} until the fade is stopped."
            : $"The lights start on {Quote(from.Name)} and move smoothly to {target} over {time}, then stay on {target} until the fade is stopped.";
    }

    private List<FadeNotice> BuildNotices(SceneFunction? from, SceneFunction? to)
    {
        var notices = new List<FadeNotice>();
        if (FromSceneId != Guid.Empty && from is null)
        {
            notices.Add(new FadeNotice(
                $"The From scene was deleted, so the fade starts from what the lights show now. Pick another scene or {Quote("Current look")}.",
                true));
        }

        if (ToSceneId != Guid.Empty && to is null)
        {
            notices.Add(new FadeNotice("The To scene was deleted: this fade does nothing until you pick another scene.", true));
        }

        if (to is not null && LightNames(to).Count == 0)
        {
            notices.Add(new FadeNotice($"{Quote(to.Name)} has no lights yet, so this fade changes nothing. Add lights to it in the scene editor.", true));
        }

        if (from is not null && to is not null && from.Id == to.Id)
        {
            notices.Add(new FadeNotice("From and To are the same scene, so the lights do not change.", false));
            return notices;
        }

        if (from is null || to is null)
        {
            return notices;
        }

        var fromIds = from.TargetFixtureIds().ToHashSet();
        var toIds = to.TargetFixtureIds().ToHashSet();
        var fromCount = LightNames(from).Count;
        var toCount = LightNames(to).Count;
        var onlyTo = LightNames(to, id => !fromIds.Contains(id));
        var onlyFrom = LightNames(from, id => !toIds.Contains(id));

        if (toCount == 0)
        {
            return notices;
        }

        if (fromCount == 0)
        {
            notices.Add(new FadeNotice(
                $"{Quote(from.Name)} has no lights, so the lights of {Quote(to.Name)} fade in from what they show now.",
                false));
            return notices;
        }

        if (onlyFrom.Count == fromCount && onlyTo.Count == toCount)
        {
            // Nothing in common: say it once instead of listing both sides.
            notices.Add(new FadeNotice(
                $"{Quote(from.Name)} and {Quote(to.Name)} use different lights: the lights of {Quote(to.Name)} fade in from what "
                + $"they show now, and the lights of {Quote(from.Name)} fade back to how they would look without this fade.",
                false));
            return notices;
        }

        if (onlyTo.Count > 0)
        {
            notices.Add(new FadeNotice(
                onlyTo.Count == 1
                    ? $"1 light is only in To ({ListNames(onlyTo)}): it fades in from what it shows now."
                    : $"{onlyTo.Count} lights are only in To ({ListNames(onlyTo)}): they fade in from what they show now.",
                false));
        }

        if (onlyFrom.Count > 0)
        {
            notices.Add(new FadeNotice(
                onlyFrom.Count == 1
                    ? $"1 light is only in From ({ListNames(onlyFrom)}): it fades back to how it would look without this fade."
                    : $"{onlyFrom.Count} lights are only in From ({ListNames(onlyFrom)}): they fade back to how they would look without this fade.",
                false));
        }

        return notices;
    }

    private List<string> LightNames(SceneFunction scene, Func<Guid, bool>? include = null)
    {
        var names = new List<string>();
        foreach (var id in scene.TargetFixtureIds())
        {
            if ((include is null || include(id)) && Project.FindFixture(id) is { } fixture)
            {
                names.Add(fixture.Name);
            }
        }

        return names;
    }

    private void UpdateGradient(SceneFunction? from, SceneFunction? to)
    {
        var end = to is null ? null : ScenePreviewColor.Estimate(to, Project);
        HasGradient = end is not null;
        if (end is not { } endColor)
        {
            GradientStart = GradientEnd = "#00000000";
            return;
        }

        GradientEnd = Argb(0xFF, endColor);
        var start = from is null ? null : ScenePreviewColor.Estimate(from, Project);

        // No From colour: the To colour grows out of nothing (fully transparent), like a fade-in over what is there.
        GradientStart = start is { } startColor ? Argb(0xFF, startColor) : Argb(0x00, endColor);
    }

    private static string Argb(byte alpha, RgbColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    private static string Quote(string name) => $"“{name}”";
}
