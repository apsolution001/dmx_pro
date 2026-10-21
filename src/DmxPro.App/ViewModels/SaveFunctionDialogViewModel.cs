using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// "Save as function" name dialog (Shows block, Test &amp; Preview): a required name, an optional fade in / out pair and
/// a hint. The caller creates the function from <see cref="TrimmedName"/> (and the fades) when the dialog returns true.
/// A name already used in the Functions list is allowed but flagged, so two functions are never mixed up silently.
/// </summary>
public sealed partial class SaveFunctionDialogViewModel : ObservableObject
{
    private readonly Func<string, bool> _isNameTaken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimmedName), nameof(NameError), nameof(HasNameError), nameof(NameWarning), nameof(HasNameWarning), nameof(CanSave))]
    private string _name;

    [ObservableProperty]
    private double? _fadeInSeconds = 0;

    [ObservableProperty]
    private double? _fadeOutSeconds = 0;

    /// <param name="title">Window title, e.g. "Save as function".</param>
    /// <param name="message">One line above the name box: what is saved and what stays unchanged.</param>
    /// <param name="suggestedName">Prefilled (and selected) name.</param>
    /// <param name="showFades">Show the fade in / out boxes (scenes from the Test page).</param>
    /// <param name="isNameTaken">True when a function in the Functions list already has the (trimmed) name.</param>
    public SaveFunctionDialogViewModel(string title, string message, string suggestedName, bool showFades, Func<string, bool> isNameTaken)
    {
        Title = title;
        Message = message;
        ShowFades = showFades;
        _isNameTaken = isNameTaken;
        _name = suggestedName;
    }

    public string Title { get; }

    public string Message { get; }

    public bool ShowFades { get; }

    public string TrimmedName => Name?.Trim() ?? string.Empty;

    public bool CanSave => TrimmedName.Length > 0;

    /// <summary>Inline validation under the name box (blank name), or null.</summary>
    public string? NameError => TrimmedName.Length == 0 ? "Enter a name." : null;

    public bool HasNameError => NameError is not null;

    /// <summary>Non-blocking note when the name is already used in the Functions list.</summary>
    public string? NameWarning => TrimmedName.Length > 0 && _isNameTaken(TrimmedName)
        ? $"A function called \"{TrimmedName}\" already exists. Saving adds a second one."
        : null;

    public bool HasNameWarning => NameWarning is not null;

    /// <summary>Fade in seconds, 0 when empty or invalid (the Core clamps the range).</summary>
    public double FadeIn => FadeInSeconds is { } v && double.IsFinite(v) ? Math.Max(0, v) : 0;

    /// <summary>Fade out seconds, 0 when empty or invalid (the Core clamps the range).</summary>
    public double FadeOut => FadeOutSeconds is { } v && double.IsFinite(v) ? Math.Max(0, v) : 0;

    /// <summary>Called by Save (button or Enter): true when the dialog may close with this name.</summary>
    public bool TryAccept() => CanSave;
}
