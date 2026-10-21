using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Base of every function editor shown below the Functions tree (and over the Shows timeline for a show's own copy):
/// the shared toolbar state (name, Save / Revert / Close, unsaved changes, "Show in function list" for show copies).
/// Editors work on a draft; nothing reaches the project until <see cref="Save"/>. While live mode is off the open
/// editor's draft is shown on the lights automatically (<see cref="Services.FunctionPreviewService"/> listens to
/// <see cref="DraftChanged"/> and sends <see cref="BuildPreviewFunction"/> to the engine preview layer).
/// </summary>
public abstract partial class FunctionEditorViewModel : ObservableObject
{
    private bool _loading;

    // Set only while BuildPreviewFunction runs: ApplyDraft then writes into this copy instead of Original.
    private LightFunction? _previewTarget;

    /// <summary>Where the editor's draft is shown ("Showing on lights", "Output off — not on lights", …); null hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewStatus))]
    private string? _previewStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand), nameof(RevertCommand))]
    private bool _isDirty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _showInFunctionList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnverifiedWarning))]
    private string? _unverifiedWarning;

    protected FunctionEditorViewModel(Project project, LightFunction original)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(original);
        Project = project;
        Original = original;
        ShowName = original.OwnerShowId is { } showId ? project.FindShow(showId)?.Name ?? "(deleted show)" : null;
    }

    /// <summary>The function in the project; only written by <see cref="Save"/>.</summary>
    public LightFunction Original { get; }

    public FunctionType Type => Original.Type;

    public string TypeName => FunctionRowViewModel.TypeDisplayName(Type);

    /// <summary><see cref="Controls.DmxIcon"/> kind of the function type.</summary>
    public string IconKind => FunctionRowViewModel.TypeIconKind(Type);

    public string Title => $"{TypeName} · {Original.Name}";

    /// <summary>"Chaser editor": automation name of the editor area.</summary>
    public string EditorAutomationName => $"{TypeName} editor";

    /// <summary>Name of the show that owns this function as its own copy; null for a library function.</summary>
    public string? ShowName { get; }

    public bool IsShowCopy => ShowName is not null;

    /// <summary>"Own copy in show Wedding: changes never touch the original." Null for a library function.</summary>
    public string? ShowCopyText => ShowName is null ? null : $"Own copy in show \"{ShowName}\": changes never touch the original.";

    public bool HasUnverifiedWarning => UnverifiedWarning is not null;

    public bool HasPreviewStatus => PreviewStatus is not null;

    /// <summary>Same command as <see cref="SaveChangesCommand"/>.</summary>
    public IRelayCommand SaveCommand => SaveChangesCommand;

    /// <summary>True while the draft is (re)loaded: edits made then do not count as changes.</summary>
    protected bool IsLoading => _loading;

    protected Project Project { get; }

    /// <summary>
    /// The function the draft settings are written to by <see cref="ApplyDraft"/>: <see cref="Original"/> on save, the
    /// preview copy while <see cref="BuildPreviewFunction"/> runs. Editors read the saved function through
    /// <see cref="Original"/> in <see cref="LoadDraft"/> and write through this in <see cref="ApplyDraft"/>.
    /// </summary>
    protected LightFunction Target => _previewTarget ?? Original;

    /// <summary>Raised after a successful save, so the page can mark the project changed and refresh its lists.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Raised after every change to the draft (each edit, even when it is already unsaved) and after a revert, so the
    /// live preview can follow it. Not raised while the draft is (re)loaded.
    /// </summary>
    public event EventHandler? DraftChanged;

    /// <summary>Raised by <see cref="CloseCommand"/>. The page decides (unsaved-changes prompt) and closes the editor.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The list renamed the function while the editor is open: take the new name without making the draft dirty.</summary>
    public void SyncName(string name)
    {
        var wasDirty = IsDirty;
        _loading = true;
        Name = name;
        _loading = false;
        IsDirty = wasDirty;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Writes the draft into the function. A blank name keeps the old name.</summary>
    public bool Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = Original.Name;
        }

        Original.Name = Name.Trim();
        ApplyDraft();
        if (IsShowCopy)
        {
            Original.ShowInFunctionList = ShowInFunctionList;
        }

        IsDirty = false;
        OnPropertyChanged(nameof(Title));
        Saved?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// The draft as a stand-alone function for the live preview: a deep copy of <see cref="Original"/> with the same id
    /// and the draft applied. Neither the project nor <see cref="Original"/> is changed.
    /// </summary>
    public LightFunction BuildPreviewFunction()
    {
        var copy = Original.CloneAsNew();
        copy.Id = Original.Id;
        copy.CopiedFromId = Original.CopiedFromId;
        copy.Name = Name;
        _previewTarget = copy;
        try
        {
            ApplyDraft();
        }
        finally
        {
            _previewTarget = null;
        }

        return copy;
    }

    /// <summary>Called when the editor goes away: unsubscribe from long-lived services here.</summary>
    public virtual void Detach()
    {
    }

    /// <summary>
    /// The project's fixtures or groups changed (e.g. a light was added while this editor was open): refresh the lists
    /// of lights and groups to choose from. The function's own selection is kept.
    /// </summary>
    public virtual void RefreshPatch()
    {
    }

    /// <summary>Copies the draft's type-specific settings into <see cref="Original"/>.</summary>
    protected abstract void ApplyDraft();

    /// <summary>(Re)builds the draft from <see cref="Original"/>; runs with <see cref="IsLoading"/> set.</summary>
    protected abstract void LoadDraft();

    /// <summary>Loads (or reverts to) the saved function. Derived constructors call this once at their end.</summary>
    protected void LoadFromOriginal()
    {
        _loading = true;
        try
        {
            Name = Original.Name;
            ShowInFunctionList = Original.ShowInFunctionList;
            LoadDraft();
        }
        finally
        {
            _loading = false;
        }

        IsDirty = false;
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stores a number typed into a field: rounded and clamped to [<paramref name="min"/>, <paramref name="max"/>];
    /// empty or invalid input keeps the old value. Always re-raises the property so the box shows what was stored.
    /// </summary>
    protected void SetNumber(ref double field, double? value, double min, double max, [CallerMemberName] string? propertyName = null)
    {
        var next = value is { } v && double.IsFinite(v) ? FunctionEditing.Clamp(v, min, max) : field;
        var changed = next != field;
        field = next;
        OnPropertyChanged(propertyName);
        if (changed)
        {
            MarkDirty();
        }
    }

    protected void MarkDirty()
    {
        if (!_loading)
        {
            IsDirty = true;
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void SaveChanges() => Save();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert() => LoadFromOriginal();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnShowInFunctionListChanged(bool value) => MarkDirty();
}
