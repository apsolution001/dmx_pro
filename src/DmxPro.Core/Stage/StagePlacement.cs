namespace DmxPro.Core.Stage;

/// <summary>
/// Where one fixture sits on the virtual stage. Stage coordinates are metres: <see cref="X"/> from the left edge of the
/// stage (audience view), <see cref="Y"/> from the front edge (downstage = 0) towards the back.
/// </summary>
public sealed class StagePlacement
{
    /// <summary>The fixture (<see cref="Patching.Fixture.Id"/>).</summary>
    public Guid FixtureId { get; set; }

    /// <summary>Metres from the stage's left edge.</summary>
    public double X { get; set; }

    /// <summary>Metres from the stage's front edge.</summary>
    public double Y { get; set; }

    /// <summary>Rotation of the beam in degrees (0 = pointing to the audience); cosmetic in the 2D view.</summary>
    public double Rotation { get; set; }

    /// <summary>Deep copy.</summary>
    public StagePlacement Clone() => (StagePlacement)MemberwiseClone();
}
