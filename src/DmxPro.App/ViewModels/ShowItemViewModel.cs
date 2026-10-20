using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Shows;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One function block on a show track. Layout values (<see cref="Left"/>, <see cref="Width"/>, fade wedges) are in
/// timeline pixels for the current zoom. The editable properties (Start, Length, fades, name, "Show in function list")
/// feed the Properties panel and write through <see cref="ShowsViewModel"/>, which marks the project changed.
/// </summary>
public sealed partial class ShowItemViewModel : ObservableObject
{
    private readonly ShowsViewModel _owner;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private double _left;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _fadeInWidth;

    [ObservableProperty]
    private double _fadeOutWidth;

    public ShowItemViewModel(ShowsViewModel owner, ShowTrackViewModel track, ShowItem item, LightFunction? function, RgbColor? color)
    {
        _owner = owner;
        Track = track;
        Item = item;
        Function = function;
        SetColor(color);
    }

    public ShowItem Item { get; }

    public ShowTrackViewModel Track { get; }

    /// <summary>The show's own copy this item plays; null when the project references a missing function.</summary>
    public LightFunction? Function { get; }

    public string Name => Function?.Name ?? "(missing function)";

    public string TypeName => Function is null ? "Missing" : ShowsLibraryItem.TypeLabel(Function.Type);

    /// <summary>"#RRGGBB" light colour of the block, or null to use the accent colour.</summary>
    public string? ColorHex { get; private set; }

    public bool HasColor => ColorHex is not null;

    /// <summary>True when the block colour is light, so its label is drawn dark.</summary>
    public bool IsLightColor { get; private set; }

    public string ToolTip =>
        $"{Name} ({TypeName})\n{ShowsViewModel.FormatTime(Item.StartSeconds)} – {ShowsViewModel.FormatTime(Item.EndSeconds)}";

    public string FunctionName
    {
        get => Name;
        set => _owner.RenameItemFunction(this, value);
    }

    public double? StartSeconds
    {
        get => Item.StartSeconds;
        set => _owner.SetItemTiming(this, start: value);
    }

    public double? LengthSeconds
    {
        get => Item.LengthSeconds;
        set => _owner.SetItemTiming(this, length: value);
    }

    public double? FadeInSeconds
    {
        get => Item.FadeInSeconds;
        set => _owner.SetItemTiming(this, fadeIn: value);
    }

    public double? FadeOutSeconds
    {
        get => Item.FadeOutSeconds;
        set => _owner.SetItemTiming(this, fadeOut: value);
    }

    public bool ShowInFunctionList
    {
        get => Function?.ShowInFunctionList ?? false;
        set => _owner.SetItemShowInFunctionList(this, value);
    }

    public bool HasFunction => Function is not null;

    /// <summary>Where the item's function was copied from, for the Properties hint.</summary>
    public string CopyHint => Function?.CopiedFromId is null
        ? "This function was made in this show."
        : "This is the show's own copy: changing it does not affect the original function.";

    public string TimingText =>
        $"{ShowsViewModel.FormatTime(Item.StartSeconds)} – {ShowsViewModel.FormatTime(Item.EndSeconds)}";

    /// <summary>Re-reads the model after an edit and lays the block out for <paramref name="pixelsPerSecond"/>.</summary>
    public void Refresh(double pixelsPerSecond, RgbColor? color)
    {
        SetColor(color);
        Layout(Item.StartSeconds, Item.LengthSeconds, pixelsPerSecond);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Moves the block on screen only (live drag preview); the model is unchanged until the drop.</summary>
    public void Layout(double startSeconds, double lengthSeconds, double pixelsPerSecond)
    {
        Left = startSeconds * pixelsPerSecond;
        Width = Math.Max(4, lengthSeconds * pixelsPerSecond);
        FadeInWidth = Math.Min(Width, Item.FadeInSeconds * pixelsPerSecond);
        FadeOutWidth = Math.Min(Width - FadeInWidth, Item.FadeOutSeconds * pixelsPerSecond);
    }

    private void SetColor(RgbColor? color)
    {
        ColorHex = color?.ToHex();
        IsLightColor = color?.IsLight ?? false;
    }
}
