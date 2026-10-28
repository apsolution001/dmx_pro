using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>A sequence that still reaches an editor is converted by <see cref="SequenceMigration"/>, same as on load.</summary>
public sealed class FunctionEditorFactorySequenceTests : IDisposable
{
    private readonly AppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static (Project Project, SequenceFunction Sequence) Build(bool damaged)
    {
        var project = new Project();
        var profile = new FixtureProfile
        {
            Id = "t",
            Manufacturer = "T",
            Model = "T",
            Channels = [new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer }],
            Modes = [new() { Name = "1ch", Channels = ["dim"] }],
        };
        var par = project.AddFixtures(profile, "1ch", 1, 1, 1, "PAR")[0];
        var sequence = new SequenceFunction { Name = "Seq" };
        var step = new SequenceStep { Name = "A", HoldSeconds = 2 };
        step.SetValue(par.Id, "dim", 100);
        sequence.Steps.Add(step);
        if (damaged)
        {
            sequence.Steps.Add(null!);
            sequence.Steps.Add(new SequenceStep { Name = "B", Values = null! });
        }

        project.Functions.Add(sequence);
        project.Functions.Add(new SceneFunction { Name = "After" });
        return (project, sequence);
    }

    [Fact]
    public void Create_SequenceWithNullStepAndNullValues_DoesNotThrow()
    {
        var (project, sequence) = Build(damaged: true);

        var vm = FunctionEditorFactory.Create(project, sequence, _rig.Settings, _rig.Clipboard);

        Assert.IsType<ChaserEditorViewModel>(vm);
        Assert.DoesNotContain(project.Functions, f => f is SequenceFunction);
    }

    [Fact]
    public void Create_Sequence_ProducesSameFunctionsAsMigrate()
    {
        var (viaFactory, sequence) = Build(damaged: true);
        var (viaMigrate, _) = Build(damaged: true);

        FunctionEditorFactory.Create(viaFactory, sequence, _rig.Settings, _rig.Clipboard);
        SequenceMigration.Migrate(viaMigrate);

        Assert.Equal(viaMigrate.Functions.Count, viaFactory.Functions.Count);
        for (var i = 0; i < viaMigrate.Functions.Count; i++)
        {
            Assert.Equal(viaMigrate.Functions[i].GetType(), viaFactory.Functions[i].GetType());
            Assert.Equal(viaMigrate.Functions[i].Name, viaFactory.Functions[i].Name);
        }

        var chaser = Assert.IsType<ChaserFunction>(viaFactory.Functions[0]);
        Assert.Equal(sequence.Id, chaser.Id);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.Equal("After", viaFactory.Functions[^1].Name);
    }
}
