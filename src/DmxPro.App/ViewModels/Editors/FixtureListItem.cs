namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One light (fixture) an effect or sequence uses, in play order: name, "U1 · 1–8 · Generic RGBW" detail. A light that
/// no longer exists stays listed (so it can be removed) as "(deleted light)" with <see cref="IsMissing"/> set.
/// </summary>
public sealed record FixtureListItem(Guid Id, int Number, string Name, string Detail, bool IsMissing)
{
    public string AutomationName => $"{Number}. {Name}, {Detail}";
}
