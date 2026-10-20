using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;

namespace DmxPro.Engine.Tests;

/// <summary>Extra profiles for effect tests: an RGB fixture without dimmer and a dimmer-only fixture.</summary>
internal static class EffectProfiles
{
    /// <summary>3 channels: green, red, blue (deliberately not R-G-B order).</summary>
    public static FixtureProfile GrbOnly() => new()
    {
        Id = "test-grb",
        Manufacturer = "Test",
        Model = "GRB",
        Channels =
        [
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
        ],
        Modes = [new() { Name = "3ch", Channels = ["g", "r", "b"] }],
    };

    /// <summary>1 channel: dimmer, default 0.</summary>
    public static FixtureProfile DimmerOnly() => new()
    {
        Id = "test-dimmer",
        Manufacturer = "Test",
        Model = "Dimmer",
        Channels = [new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer }],
        Modes = [new() { Name = "1ch", Channels = ["dim"] }],
    };

    /// <summary>Adds a GRB fixture at <paramref name="address"/> (green there, red +1, blue +2).</summary>
    public static Fixture AddGrb(this TestRig rig, int address) =>
        rig.Project.AddFixtures(GrbOnly(), "3ch", 1, 1, address, "GRB")[0];

    /// <summary>Adds a dimmer-only fixture at <paramref name="address"/>.</summary>
    public static Fixture AddDimmer(this TestRig rig, int address) =>
        rig.Project.AddFixtures(DimmerOnly(), "1ch", 1, 1, address, "Dimmer")[0];

    /// <summary>Adds <paramref name="function"/> to the rig's project and returns it.</summary>
    public static T Add<T>(this TestRig rig, T function)
        where T : LightFunction
    {
        rig.Project.Functions.Add(function);
        return function;
    }
}
