namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One fixture an effect or sequence uses, in play order: name, "U1 · 1–8 · Generic RGBW" detail and whether its
/// profile is unverified. A fixture that no longer exists stays listed (so it can be removed) as "(missing fixture)".
/// </summary>
public sealed record FixtureListItem(Guid Id, int Number, string Name, string Detail, bool Unverified, bool IsMissing)
{
    public string AutomationName => $"{Number}. {Name}, {Detail}";
}
