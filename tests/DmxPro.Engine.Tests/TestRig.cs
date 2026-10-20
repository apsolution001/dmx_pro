using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Engine.Tests;

/// <summary>
/// A project with one 8-channel RGBW fixture at universe 1, address 10, plus helpers to drive the engine manually.
/// 8ch layout: red 10, green 11, blue 12, white 13, dimmer 14 (default 255), strobe 15, macro 16, speed 17.
/// </summary>
internal sealed class TestRig
{
    public const int Red = 10;
    public const int Green = 11;
    public const int Blue = 12;
    public const int White = 13;
    public const int Dimmer = 14;
    public const int Strobe = 15;
    public const int Macro = 16;
    public const int Speed = 17;

    public static readonly TimeSpan Frame = TimeSpan.FromSeconds(0.025);

    public TestRig(string mode = "8ch", int startAddress = 10)
    {
        Fixture = Project.AddFixtures(Profile(), mode, 1, 1, startAddress, "Par")[0];
        Engine.SnapshotInterval = TimeSpan.Zero;
    }

    public Project Project { get; } = new();

    public Fixture Fixture { get; }

    public DmxEngine Engine { get; } = new();

    public static FixtureProfile Profile() => new()
    {
        Id = "test-rgbw",
        Manufacturer = "Test",
        Model = "RGBW",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
            new() { Key = "w", Name = "White", Type = ChannelType.White },
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer, Default = 255 },
            new()
            {
                Key = "strobe",
                Name = "Strobe",
                Type = ChannelType.Strobe,
                Capabilities =
                [
                    new() { Min = 0, Max = 9, Label = "No strobe", Safe = true },
                    new() { Min = 10, Max = 255, Label = "Strobe slow to fast" },
                ],
            },
            new() { Key = "macro", Name = "Macro", Type = ChannelType.ColorMacro },
            new() { Key = "speed", Name = "Speed", Type = ChannelType.Speed },
        ],
        Modes =
        [
            new() { Name = "5ch", Channels = ["dim", "r", "g", "b", "w"] },
            new() { Name = "8ch", Channels = ["r", "g", "b", "w", "dim", "strobe", "macro", "speed"] },
        ],
    };

    /// <summary>Adds a scene setting the given channel keys of the rig fixture.</summary>
    public SceneFunction Scene(string name, params (string Key, byte Value)[] values)
    {
        var scene = new SceneFunction { Name = name };
        foreach (var (key, value) in values)
        {
            scene.SetValue(Fixture.Id, key, value);
        }

        Project.Functions.Add(scene);
        return scene;
    }

    /// <summary>Loads the project into the engine and applies it with a zero-length tick.</summary>
    public void Load()
    {
        Engine.LoadProject(Project);
        Engine.Tick(TimeSpan.Zero);
    }

    /// <summary>Re-resolves the edited project, keeping what runs, and applies it with a zero-length tick.</summary>
    public void RefreshAndApply()
    {
        Engine.RefreshPatch(Project);
        Engine.Tick(TimeSpan.Zero);
    }

    public void Tick(double seconds) => Engine.Tick(TimeSpan.FromSeconds(seconds));

    public void Tick() => Engine.Tick(Frame);

    public byte At(int address, int universe = 1) => Engine.GetSnapshot()!.GetValue(universe, address);

    public byte[] Universe(int universe = 1) => Engine.GetSnapshot()!.GetUniverse(universe).ToArray();

    /// <summary>The idle frame: every channel at its profile default (only the 8ch dimmer is non-zero).</summary>
    public static byte[] DefaultsFrame()
    {
        var frame = new byte[DmxAddress.UniverseSize];
        frame[DmxAddress.ToIndex(Dimmer)] = 255;
        return frame;
    }
}
