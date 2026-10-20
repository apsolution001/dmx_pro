using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>Manufacturer folder of the profile tree. "Generic" (the built-in templates) always comes first.</summary>
public sealed class ProfileTreeGroup : ProfileTreeNode
{
    public const string GenericManufacturer = "Generic";

    public const string UnknownManufacturer = "Other";

    public ProfileTreeGroup(string name, IReadOnlyList<ProfileTreeItem> items)
    {
        Name = name;
        Items = items;
    }

    public override string Name { get; }

    public IReadOnlyList<ProfileTreeItem> Items { get; }

    /// <summary>
    /// Groups the profiles matching <paramref name="search"/> (model, manufacturer or type) by manufacturer.
    /// Generic profiles are ordered by size (Dimmer, RGB, RGBA, RGBW, …); other models alphabetically.
    /// </summary>
    public static IReadOnlyList<ProfileTreeGroup> Build(IEnumerable<FixtureProfile> profiles, string search)
    {
        var query = search.Trim();
        return profiles
            .Where(p => query.Length == 0 ||
                        p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Type.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Manufacturer) ? UnknownManufacturer : p.Manufacturer.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => IsGeneric(g.Key) ? 0 : 1)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProfileTreeGroup(g.Key, Order(g, IsGeneric(g.Key)).Select(p => new ProfileTreeItem(p)).ToList()))
            .ToList();
    }

    private static bool IsGeneric(string manufacturer) =>
        string.Equals(manufacturer, GenericManufacturer, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<FixtureProfile> Order(IEnumerable<FixtureProfile> profiles, bool generic) => generic
        ? profiles.OrderBy(p => p.Modes.Count == 0 ? int.MaxValue : p.Modes.Min(m => m.ChannelCount))
            .ThenBy(p => p.Model, StringComparer.OrdinalIgnoreCase)
        : profiles.OrderBy(p => p.Model, StringComparer.OrdinalIgnoreCase);
}
