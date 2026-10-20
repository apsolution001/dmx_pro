using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Patching;
using DmxPro.Core.Stage;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One patched light on the Stage page: a row of the lights list and, when <see cref="IsDrawable"/> and
/// <see cref="IsPlaced"/>, a light drawn on the stage plan.
/// </summary>
/// <remarks>
/// <see cref="X"/>, <see cref="Y"/>, <see cref="Rotation"/> and <see cref="Current"/> are plain properties without change
/// notification on purpose: they change per frame / per mouse move and are read directly by the stage canvas, which is
/// told to update through <see cref="StagePageViewModel"/> events (no binding traffic, no allocations per frame).
/// </remarks>
public sealed partial class StageLightViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isPlaced;

    [ObservableProperty]
    private bool _isSelected;

    public StageLightViewModel(Fixture fixture, string detail, StageColorModel? model, string? notDrawableReason)
    {
        Fixture = fixture;
        Detail = detail;
        Model = model;
        NotDrawableReason = notDrawableReason;
    }

    public Fixture Fixture { get; }

    public Guid FixtureId => Fixture.Id;

    public string Name => string.IsNullOrWhiteSpace(Fixture.Name) ? "(unnamed light)" : Fixture.Name;

    public int Universe => Fixture.Universe;

    public int StartAddress => Fixture.StartAddress;

    /// <summary>"U1 · 1 · Generic RGB" (or why the light cannot be drawn).</summary>
    public string Detail { get; }

    /// <summary>Profile-driven colour model; null when the light cannot be drawn.</summary>
    public StageColorModel? Model { get; }

    /// <summary>True when the light can be drawn: enabled, profile and mode found.</summary>
    public bool IsDrawable => Model is not null;

    /// <summary>Why the light is not drawn (missing profile, disabled, …), or null.</summary>
    public string? NotDrawableReason { get; }

    /// <summary>Metres from the stage's left edge (valid when <see cref="IsPlaced"/>).</summary>
    public double X { get; internal set; }

    /// <summary>Metres from the stage's front edge (valid when <see cref="IsPlaced"/>).</summary>
    public double Y { get; internal set; }

    /// <summary>Beam direction in degrees, 0 = towards the audience.</summary>
    public double Rotation { get; internal set; }

    /// <summary>The look of the light in the last evaluated frame.</summary>
    public StageLightState Current { get; internal set; }

    /// <summary>Screen-reader text for the list row.</summary>
    public string AutomationName => NotDrawableReason is { } reason
        ? $"{Name}, {Detail}, not shown on the stage: {reason}"
        : $"{Name}, {Detail}, {(IsPlaced ? "on the stage" : "not placed")}";

    /// <summary>Tooltip of the list row.</summary>
    public string ToolTip => NotDrawableReason is { } reason
        ? $"{Detail}\nNot shown on the stage: {reason}"
        : IsPlaced
            ? $"{Detail}\nOn the stage. Click to select it there."
            : $"{Detail}\nNot on the stage yet: drag it onto the stage, or use Place unplaced.";

    public bool Matches(string search) =>
        Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Detail.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    partial void OnIsPlacedChanged(bool value)
    {
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(ToolTip));
    }
}
