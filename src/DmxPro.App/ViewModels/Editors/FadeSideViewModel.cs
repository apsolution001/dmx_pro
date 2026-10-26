using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One side ("From" or "To") of the fade editor: the chosen scene with its lights as colour dots, and a searchable list
/// of scenes to pick from. The owning <see cref="FadeEditorViewModel"/> stores the choice; picking a row calls back.
/// </summary>
public sealed partial class FadeSideViewModel : ObservableObject
{
    /// <summary>At most this many light dots are drawn; the rest are summed up as "+12 more".</summary>
    public const int MaxDots = 48;

    private readonly Project _project;
    private readonly Action<Guid> _pick;
    private List<FadeSceneOption> _all = [];
    private FadeSceneOption? _selected;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public FadeSideViewModel(Project project, bool isFrom, Action<Guid> pick)
    {
        _project = project;
        IsFrom = isFrom;
        _pick = pick;
    }

    public bool IsFrom { get; }

    /// <summary>"From" / "To".</summary>
    public string Title => IsFrom ? "From" : "To";

    /// <summary>"start" / "end", next to the title.</summary>
    public string Subtitle => IsFrom ? "where the lights start" : "where the lights end up";

    public string ListAutomationName => IsFrom ? "Scenes to start from" : "Scenes to end at";

    public string SearchAutomationName => IsFrom ? "Find a scene to start from" : "Find a scene to end at";

    /// <summary>The rows shown in the list (all options matching <see cref="SearchText"/>).</summary>
    public ObservableCollection<FadeSceneOption> Options { get; } = [];

    /// <summary>
    /// The chosen row. Setting it (the user clicked a row) asks the owner to store the choice. Null is ignored: the list
    /// pushes null while filtering removes the chosen row, which must not drop the choice.
    /// </summary>
    public FadeSceneOption? SelectedOption
    {
        get => _selected;
        set
        {
            if (value is not null && !ReferenceEquals(value, _selected))
            {
                _pick(value.Id);
            }
        }
    }

    /// <summary>Name shown big on the panel ("Warm red", "Current look", "Not chosen yet").</summary>
    public string DisplayName => _selected?.Name ?? "Not chosen yet";

    /// <summary>Line under the name: what the choice means.</summary>
    public string DisplayDetail => _selected switch
    {
        null => "Pick a scene in the list below.",
        { IsCurrentLook: true } => "Starts from whatever the lights show now.",
        { IsMissing: true } => "This scene was deleted. Pick another one below.",
        _ when Lights.Count == 0 => "This scene has no lights yet.",
        _ => _selected.Detail,
    };

    public string? DisplaySwatch => _selected?.Swatch;

    public bool IsUnset => _selected is null;

    public bool IsCurrentLook => _selected?.IsCurrentLook == true;

    public bool IsMissing => _selected?.IsMissing == true;

    /// <summary>True when the panel needs attention (nothing chosen or a deleted scene).</summary>
    public bool NeedsAttention => IsUnset || IsMissing;

    /// <summary>The chosen scene's lights as colour dots (at most <see cref="MaxDots"/>).</summary>
    public IReadOnlyList<PreviewLight> Lights { get; private set; } = [];

    public bool HasLights => Lights.Count > 0;

    /// <summary>"+12 more" when the scene has more lights than dots; null otherwise.</summary>
    public string? MoreLightsText { get; private set; }

    public bool HasNoMatches => Options.Count == 0 && _all.Count > 0;

    public string NoMatchText => $"No scene matches \"{SearchText.Trim()}\".";

    /// <summary>Replaces the rows (scenes, "Current look", deleted scenes) and shows <paramref name="selectedId"/>.</summary>
    internal void SetOptions(List<FadeSceneOption> options, Guid selectedId)
    {
        _all = options;
        ApplyFilter();
        Show(selectedId);
    }

    /// <summary>Shows the choice stored by the owner (<see cref="Guid.Empty"/> = Current look on From, nothing on To).</summary>
    internal void Show(Guid id)
    {
        _selected = _all.FirstOrDefault(o => o.Id == id && (id != Guid.Empty || o.IsCurrentLook));

        var lights = new List<PreviewLight>();
        var total = 0;
        if (_selected is { Kind: FadeSceneKind.Scene } && _project.FindFunction(id) is SceneFunction scene)
        {
            var values = scene.IndexValues();
            foreach (var fixtureId in scene.TargetFixtureIds())
            {
                if (_project.FindFixture(fixtureId) is not { } fixture)
                {
                    continue;
                }

                total++;
                if (lights.Count < MaxDots)
                {
                    lights.Add(new PreviewLight(fixture.Name, PreviewLight.ToHex(values.PreviewColor(_project, fixture))));
                }
            }
        }

        Lights = lights;
        MoreLightsText = total > lights.Count ? $"+{total - lights.Count} more" : null;

        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayDetail));
        OnPropertyChanged(nameof(DisplaySwatch));
        OnPropertyChanged(nameof(IsUnset));
        OnPropertyChanged(nameof(IsCurrentLook));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(Lights));
        OnPropertyChanged(nameof(HasLights));
        OnPropertyChanged(nameof(MoreLightsText));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        Options.Clear();
        foreach (var option in _all)
        {
            if (Matches(option, search))
            {
                Options.Add(option);
            }
        }

        // Re-select the chosen row if the filter brought it back (the list dropped its selection while it was hidden).
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(NoMatchText));
    }

    private static bool Matches(FadeSceneOption option, string search) =>
        search.Length == 0 || option.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}
