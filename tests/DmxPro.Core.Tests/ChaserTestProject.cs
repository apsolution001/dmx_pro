using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>A project with one light, a library scene "Red" and helpers to build chasers with owned steps.</summary>
internal sealed class ChaserTestProject
{
    public ChaserTestProject()
    {
        Fixture = Project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "Par")[0];
        Red = (SceneFunction)Project.CreateFunction(FunctionType.Scene, "Red");
        Red.SetValue(Fixture.Id, "r", 255);
    }

    public Project Project { get; } = new();

    public Fixture Fixture { get; }

    public SceneFunction Red { get; }

    public SceneValue RedValue(byte value) => new() { FixtureId = Fixture.Id, ChannelKey = "r", Value = value };

    /// <summary>A library chaser with one own-look step per value (red channel), committed like the editor's Save.</summary>
    public ChaserFunction ChaserWithOwnLooks(string name, params byte[] reds)
    {
        var chaser = (ChaserFunction)Project.CreateFunction(FunctionType.Chaser, name);
        var draft = Draft(chaser);
        var owned = new List<LightFunction>();
        for (var i = 0; i < reds.Length; i++)
        {
            var scene = ChaserSteps.NewOwnLookScene(chaser.Id, $"Step {i + 1}", [RedValue(reds[i])]);
            owned.Add(scene);
            draft.AddStep(scene.Id);
        }

        Project.CommitChaser(draft, owned);
        return chaser;
    }

    /// <summary>An editor-style draft: a deep copy with the same id.</summary>
    public static ChaserFunction Draft(ChaserFunction chaser)
    {
        var draft = (ChaserFunction)chaser.CloneAsNew();
        draft.Id = chaser.Id;
        draft.CopiedFromId = chaser.CopiedFromId;
        return draft;
    }

    public SceneFunction StepScene(ChaserFunction chaser, int index) =>
        Assert.IsType<SceneFunction>(Project.FindFunction(chaser.Steps[index].FunctionId));
}
