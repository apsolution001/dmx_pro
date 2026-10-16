using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>Tree node: one manufacturer and its fixture profiles (QLC+-style fixture tree).</summary>
public sealed record ManufacturerNode(string Name, IReadOnlyList<FixtureProfile> Profiles)
{
    public static IReadOnlyList<ManufacturerNode> Build(IEnumerable<FixtureProfile> profiles, string search)
    {
        var query = search.Trim();
        return profiles
            .Where(p => query.Length == 0 ||
                        p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Type.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Manufacturer) ? "Other" : p.Manufacturer)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ManufacturerNode(g.Key, g.OrderBy(p => p.Model, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }
}
