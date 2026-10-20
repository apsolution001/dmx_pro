namespace DmxPro.App.ViewModels;

/// <summary>An entry of the fixture table's universe filter; <see cref="Universe"/> null = all universes.</summary>
public sealed record UniverseFilterOption(int? Universe, string Label)
{
    public override string ToString() => Label;
}
