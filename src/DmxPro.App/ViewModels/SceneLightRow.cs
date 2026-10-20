using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One light in the scene editor's General tab list: name, profile, mode, universe/address range and whether its
/// profile is unverified (shown as a warning icon, never as a confirmed channel meaning).
/// </summary>
public sealed partial class SceneLightRow : ObservableObject
{
    /// <summary>Selected in the list (several with Ctrl/Shift); Remove takes every selected row.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public SceneLightRow(Guid fixtureId, string name, string profile, string mode, string address, bool profileUnverified)
    {
        FixtureId = fixtureId;
        Name = name;
        Profile = profile;
        Mode = mode;
        Address = address;
        ProfileUnverified = profileUnverified;
    }

    public Guid FixtureId { get; }

    public string Name { get; }

    public string Profile { get; }

    public string Mode { get; }

    /// <summary>"U1 · 1–8".</summary>
    public string Address { get; }

    public bool ProfileUnverified { get; }

    public string ProfileToolTip => ProfileUnverified
        ? $"{Profile} — not verified on a real fixture. Check the channel meanings before using this scene in a show."
        : Profile;

    public string AutomationName => ProfileUnverified
        ? $"{Name}, {Profile}, {Mode}, {Address}, profile not verified"
        : $"{Name}, {Profile}, {Mode}, {Address}";

    /// <summary>Text the General tab's search box matches against.</summary>
    internal string SearchKey => $"{Name} {Profile} {Mode} {Address}";
}
