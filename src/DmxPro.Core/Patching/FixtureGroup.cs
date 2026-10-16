namespace DmxPro.Core.Patching;

/// <summary>A named selection of fixtures. References fixture ids; never copies fixture configuration.</summary>
public sealed class FixtureGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public List<Guid> FixtureIds { get; set; } = [];
}
