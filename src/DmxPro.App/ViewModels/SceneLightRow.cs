using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One light in the scene editor's General tab list: name, profile, mode and universe/address range.
/// </summary>
public sealed partial class SceneLightRow : ObservableObject
{
    /// <summary>Selected in the list (several with Ctrl/Shift); Remove takes every selected row.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public SceneLightRow(Guid fixtureId, string name, string profile, string mode, string address)
    {
        FixtureId = fixtureId;
        Name = name;
        Profile = profile;
        Mode = mode;
        Address = address;
    }

    public Guid FixtureId { get; }

    public string Name { get; }

    public string Profile { get; }

    public string Mode { get; }

    /// <summary>"U1 · 1–8".</summary>
    public string Address { get; }

    public string ProfileToolTip => Profile;

    public string AutomationName => $"{Name}, {Profile}, {Mode}, {Address}";

    /// <summary>Text the General tab's search box matches against.</summary>
    internal string SearchKey => $"{Name} {Profile} {Mode} {Address}";
}
