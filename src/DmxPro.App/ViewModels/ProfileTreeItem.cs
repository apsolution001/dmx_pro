using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>Fixture model leaf of the profile tree.</summary>
public sealed class ProfileTreeItem : ProfileTreeNode
{
    public ProfileTreeItem(FixtureProfile profile)
    {
        Profile = profile;
        IconKind = profile.Channels.Any(c => c.Type.IsColor()) ? "Color" : "Dimmer";
    }

    public FixtureProfile Profile { get; }

    public override string Name => Profile.Model;

    /// <summary>DmxIcon kind: "Color" for colour fixtures, "Dimmer" for intensity-only ones.</summary>
    public string IconKind { get; }

    public bool IsUnverified => !Profile.Verified;

    public string ToolTip => Profile.Verified
        ? $"{Profile.DisplayName} · {Profile.Type}"
        : $"{Profile.DisplayName} · {Profile.Type}\nUnverified profile: check each channel on the real fixture.";
}
