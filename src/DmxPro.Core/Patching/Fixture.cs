namespace DmxPro.Core.Patching;

/// <summary>
/// A physical light patched into the project: which profile + mode it uses and where it sits in DMX.
/// Holds no channel meanings of its own — those always come from the profile.
/// </summary>
public sealed class Fixture
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string ProfileId { get; set; } = string.Empty;

    public string ModeName { get; set; } = string.Empty;

    /// <summary>Project universe, starting at 1.</summary>
    public int Universe { get; set; } = 1;

    /// <summary>First DMX address, 1–512.</summary>
    public int StartAddress { get; set; } = DmxAddress.Min;

    public bool Enabled { get; set; } = true;

    public string? Notes { get; set; }

    /// <summary>Copy with a new id (for duplicate / paste).</summary>
    public Fixture Duplicate() => new()
    {
        Name = Name,
        ProfileId = ProfileId,
        ModeName = ModeName,
        Universe = Universe,
        StartAddress = StartAddress,
        Enabled = Enabled,
        Notes = Notes,
    };
}
