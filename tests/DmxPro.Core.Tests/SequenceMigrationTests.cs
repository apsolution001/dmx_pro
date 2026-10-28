using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

/// <summary>Old <see cref="SequenceFunction"/>s become chasers with owned own-look step scenes (Sequence merged into Chaser).</summary>
public class SequenceMigrationTests
{
    private readonly ProjectSerializer _serializer = new();
    private readonly Project _project = new();
    private readonly Fixture _par;
    private readonly Fixture _wash;

    public SequenceMigrationTests()
    {
        var fixtures = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "PAR");
        _par = fixtures[0];
        _wash = fixtures[1];
    }

    private SequenceFunction AddSequence(string name = "Seq")
    {
        var sequence = new SequenceFunction
        {
            Name = name,
            FixtureIds = [_par.Id, _wash.Id],
            RunOrder = RunOrder.PingPong,
            Direction = RunDirection.Backward,
        };
        var a = new SequenceStep { Name = "Red hit", FadeInSeconds = 0.5, HoldSeconds = 1.5, FadeOutSeconds = 0.25 };
        a.SetValue(_par.Id, "r", 200);
        a.SetValue(_wash.Id, "dim", 90);
        var b = new SequenceStep { Name = "  ", FadeInSeconds = 0, HoldSeconds = 2, FadeOutSeconds = 1 };
        b.SetValue(_par.Id, "g", 150);
        sequence.Steps.AddRange([a, b]);
        _project.Functions.Add(sequence);
        return sequence;
    }

    private static string Save(Project project, ProjectSerializer serializer) => serializer.Serialize(project);

    [Fact]
    public void Migrate_Sequence_BecomesChaserWithSameIdNameRunOrderDirection()
    {
        var sequence = AddSequence("Party");
        var index = _project.Functions.IndexOf(sequence);

        var converted = SequenceMigration.Migrate(_project);

        Assert.Equal(1, converted);
        var chaser = Assert.IsType<ChaserFunction>(_project.FindFunction(sequence.Id));
        Assert.Same(chaser, _project.Functions[index]);
        Assert.Equal("Party", chaser.Name);
        Assert.Equal(RunOrder.PingPong, chaser.RunOrder);
        Assert.Equal(RunDirection.Backward, chaser.Direction);
        Assert.Null(chaser.OwnerShowId);
        Assert.Null(chaser.OwnerFunctionId);
        Assert.Contains(chaser, _project.FunctionListItems);
        Assert.DoesNotContain(_project.Functions, f => f is SequenceFunction);
    }

    [Fact]
    public void Migrate_EachStep_BecomesOwnedHiddenSceneWithStepValuesAndTiming()
    {
        var sequence = AddSequence();

        SequenceMigration.Migrate(_project);

        var chaser = Assert.IsType<ChaserFunction>(_project.FindFunction(sequence.Id));
        Assert.Equal(2, chaser.Steps.Count);
        Assert.Equal((0.5, 1.5, 0.25), (chaser.Steps[0].FadeInSeconds, chaser.Steps[0].HoldSeconds, chaser.Steps[0].FadeOutSeconds));
        Assert.Equal((0d, 2d, 1d), (chaser.Steps[1].FadeInSeconds, chaser.Steps[1].HoldSeconds, chaser.Steps[1].FadeOutSeconds));

        var first = Assert.IsType<SceneFunction>(_project.FindFunction(chaser.Steps[0].FunctionId));
        var second = Assert.IsType<SceneFunction>(_project.FindFunction(chaser.Steps[1].FunctionId));
        Assert.Equal("Red hit", first.Name);
        Assert.Equal("Step 2", second.Name);
        foreach (var scene in new[] { first, second })
        {
            Assert.Equal(chaser.Id, scene.OwnerFunctionId);
            Assert.Null(scene.OwnerShowId);
            Assert.False(scene.ShowInFunctionList);
            Assert.True(scene.IsHiddenFromList);
            Assert.Equal((0d, 0d), (scene.FadeInSeconds, scene.FadeOutSeconds));
            Assert.True(_project.IsOwnLookStep(chaser.Steps[scene == first ? 0 : 1], chaser.Id));
            Assert.DoesNotContain(scene, _project.FunctionListItems);
        }

        Assert.Equal((byte)200, first.GetValue(_par.Id, "r"));
        Assert.Equal((byte)90, first.GetValue(_wash.Id, "dim"));
        Assert.Equal(2, first.Values.Count);
        Assert.Equal((byte)150, Assert.Single(second.Values).Value);

        // Step scenes sit right after their chaser, in step order.
        var index = _project.Functions.IndexOf(chaser);
        Assert.Same(first, _project.Functions[index + 1]);
        Assert.Same(second, _project.Functions[index + 2]);
    }

    [Fact]
    public void Migrate_StepValues_AreClonedNotShared()
    {
        var sequence = AddSequence();
        var original = sequence.Steps[0].Values[0];

        SequenceMigration.Migrate(_project);

        var chaser = (ChaserFunction)_project.FindFunction(sequence.Id)!;
        var scene = (SceneFunction)_project.FindFunction(chaser.Steps[0].FunctionId)!;
        Assert.NotSame(original, scene.Values[0]);
        original.Value = 1;
        Assert.Equal((byte)200, scene.GetValue(_par.Id, "r"));
    }

    [Fact]
    public void Migrate_DuplicateBlankAndNullValues_LaterWinsInFirstPlaceOthersSkipped()
    {
        var sequence = new SequenceFunction { Name = "Seq" };
        var step = new SequenceStep();
        step.Values.Add(new SceneValue { FixtureId = _par.Id, ChannelKey = "r", Value = 10 });
        step.Values.Add(new SceneValue { FixtureId = _par.Id, ChannelKey = "g", Value = 20 });
        step.Values.Add(new SceneValue { FixtureId = _par.Id, ChannelKey = "r", Value = 30 });
        step.Values.Add(new SceneValue { FixtureId = _par.Id, ChannelKey = " ", Value = 40 });
        step.Values.Add(null!);
        sequence.Steps.Add(step);
        _project.Functions.Add(sequence);

        SequenceMigration.Migrate(_project);

        var chaser = (ChaserFunction)_project.FindFunction(sequence.Id)!;
        var scene = (SceneFunction)_project.FindFunction(chaser.Steps[0].FunctionId)!;
        Assert.Equal([("r", (byte)30), ("g", (byte)20)], scene.Values.Select(v => (v.ChannelKey, v.Value)));
    }

    [Fact]
    public void Migrate_NullStepOrNullValues_DroppedOrEmptyWithoutThrowing()
    {
        var sequence = new SequenceFunction { Name = "Seq" };
        sequence.Steps.Add(null!);
        sequence.Steps.Add(new SequenceStep { Values = null!, HoldSeconds = 3 });
        _project.Functions.Add(sequence);

        SequenceMigration.Migrate(_project);

        var chaser = (ChaserFunction)_project.FindFunction(sequence.Id)!;
        var step = Assert.Single(chaser.Steps);
        Assert.Equal(3, step.HoldSeconds);
        var scene = (SceneFunction)_project.FindFunction(step.FunctionId)!;
        Assert.Equal("Step 2", scene.Name);
        Assert.Empty(scene.Values);
    }

    [Fact]
    public void Migrate_NoSteps_EmptyChaserNoScenes()
    {
        var sequence = new SequenceFunction { Name = "Empty" };
        _project.Functions.Add(sequence);

        SequenceMigration.Migrate(_project);

        var chaser = Assert.IsType<ChaserFunction>(Assert.Single(_project.Functions));
        Assert.Empty(chaser.Steps);
    }

    [Fact]
    public void Migrate_NoSequences_ReturnsZeroAndChangesNothing()
    {
        var scene = _project.CreateScene("Red");
        var chaser = (ChaserFunction)_project.CreateFunction(FunctionType.Chaser, "Chase");
        chaser.AddStep(scene.Id);
        var before = Save(_project, _serializer);

        Assert.Equal(0, SequenceMigration.Migrate(_project));

        Assert.Equal(before, Save(_project, _serializer));
    }

    [Fact]
    public void Migrate_SeveralSequences_AllConvertedAndCounted()
    {
        var one = AddSequence("One");
        var between = _project.CreateScene("Between");
        var two = AddSequence("Two");

        Assert.Equal(2, SequenceMigration.Migrate(_project));

        Assert.IsType<ChaserFunction>(_project.FindFunction(one.Id));
        Assert.IsType<ChaserFunction>(_project.FindFunction(two.Id));
        Assert.Same(between, _project.FindFunction(between.Id));
        Assert.Equal(["One", "Between", "Two"], _project.FunctionListItems.Select(f => f.Name));
        Assert.Equal(7, _project.Functions.Count);
    }

    [Fact]
    public void Migrate_OwnershipKept_SequenceOwnedByShow_ChaserOwnedByShow()
    {
        var library = AddSequence();
        var show = _project.CreateShow("Show");
        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, library.Id, 0, 10);
        var showCopy = Assert.IsType<SequenceFunction>(_project.FindFunction(item.FunctionId));
        Assert.Equal(show.Id, showCopy.OwnerShowId);

        Assert.Equal(2, SequenceMigration.Migrate(_project));

        var chaser = Assert.IsType<ChaserFunction>(_project.FindFunction(item.FunctionId));
        Assert.Equal(show.Id, chaser.OwnerShowId);
        Assert.Null(chaser.OwnerFunctionId);
        Assert.False(chaser.ShowInFunctionList);
        Assert.Equal(library.Id, chaser.CopiedFromId);
        Assert.DoesNotContain(chaser, _project.FunctionListItems);
        Assert.All(_project.OwnedFunctions(chaser.Id), f =>
        {
            Assert.Null(f.OwnerShowId);
            Assert.Equal(chaser.Id, f.OwnerFunctionId);
        });
        Assert.Equal(2, _project.OwnedFunctions(chaser.Id).Count);

        // Deleting the show takes the chaser copy and its step scenes with it.
        Assert.True(_project.DeleteShow(show.Id));
        Assert.Null(_project.FindFunction(chaser.Id));
        Assert.DoesNotContain(_project.Functions, f => f.OwnerFunctionId == chaser.Id);
    }

    [Fact]
    public void Migrate_SequenceOwnedByFunction_KeepsItsOwner()
    {
        var owner = (ChaserFunction)_project.CreateFunction(FunctionType.Chaser, "Outer");
        var sequence = AddSequence();
        sequence.OwnerFunctionId = owner.Id;
        sequence.ShowInFunctionList = false;
        owner.AddStep(sequence.Id);

        SequenceMigration.Migrate(_project);

        var chaser = Assert.IsType<ChaserFunction>(_project.FindFunction(sequence.Id));
        Assert.Equal(owner.Id, chaser.OwnerFunctionId);
        Assert.Equal(3, _project.OwnedFunctions(owner.Id).Count);
        Assert.Empty(_project.ValidateFunctionReferences());
    }

    [Fact]
    public void Migrate_ReferencesToSequenceId_StillResolve()
    {
        var sequence = AddSequence();
        var outer = (ChaserFunction)_project.CreateFunction(FunctionType.Chaser, "Outer");
        outer.AddStep(sequence.Id);
        var page = _project.AddConsolePage("Main");
        var widget = _project.AddConsoleWidget(page.Id, new FunctionWidget { FunctionId = sequence.Id, Label = "Seq" });
        var show = _project.CreateShow("Show");
        var item = _project.CreateFunctionInShow(show.Id, show.Tracks[0].Id, FunctionType.Scene, "x", 0, 1);
        item.FunctionId = sequence.Id;

        SequenceMigration.Migrate(_project);

        Assert.IsType<ChaserFunction>(_project.FindFunction(((FunctionWidget)widget).FunctionId));
        Assert.IsType<ChaserFunction>(_project.FindFunction(outer.Steps[0].FunctionId));
        Assert.IsType<ChaserFunction>(_project.FindFunction(item.FunctionId));
        Assert.Empty(_project.ValidateConsole());
    }

    [Fact]
    public void Migrate_Result_HasNoReferenceOrOwnershipWarnings()
    {
        AddSequence("One");
        var show = _project.CreateShow("Show");
        _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, AddSequence("Two").Id, 0, 5);

        SequenceMigration.Migrate(_project);

        Assert.Empty(_project.ValidateFunctionReferences());
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void Migrate_Twice_SecondRunConvertsNothing()
    {
        AddSequence();
        SequenceMigration.Migrate(_project);
        var once = Save(_project, _serializer);

        Assert.Equal(0, SequenceMigration.Migrate(_project));

        Assert.Equal(once, Save(_project, _serializer));
    }

    [Fact]
    public void Load_OldFileWithSequence_SavesAsChaser_ReloadsIdentically()
    {
        var sequence = AddSequence();
        var page = _project.AddConsolePage("Main");
        _project.AddConsoleWidget(page.Id, new FunctionWidget { FunctionId = sequence.Id, Label = "Seq" });
        var oldFile = _serializer.Serialize(_project);
        Assert.Contains("\"type\": \"Sequence\"", oldFile, StringComparison.Ordinal);

        var loaded = _serializer.Deserialize(oldFile);

        var chaser = Assert.IsType<ChaserFunction>(loaded.Project.FindFunction(sequence.Id));
        Assert.Equal(2, chaser.Steps.Count);
        Assert.DoesNotContain(loaded.Issues, i => i.Code.StartsWith("FUNCTION_", StringComparison.Ordinal));
        var saved = _serializer.Serialize(loaded.Project);
        Assert.DoesNotContain("\"type\": \"Sequence\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"ownerFunctionId\"", saved, StringComparison.Ordinal);

        var reloaded = _serializer.Deserialize(saved);

        Assert.Equal(saved, _serializer.Serialize(reloaded.Project));
    }

    [Fact]
    public void Load_OldSequenceWithBrokenTimes_ChaserStepsGetClampedTimes()
    {
        var sequence = new SequenceFunction { Name = "Seq" };
        sequence.Steps.Add(new SequenceStep { FadeInSeconds = -1, HoldSeconds = 1e9, FadeOutSeconds = 0.5 });
        _project.Functions.Add(sequence);

        var loaded = _serializer.Deserialize(_serializer.Serialize(_project)).Project;

        var step = Assert.Single(Assert.IsType<ChaserFunction>(loaded.FindFunction(sequence.Id)).Steps);
        Assert.Equal((0d, FunctionEditing.MaxSeconds, 0.5), (step.FadeInSeconds, step.HoldSeconds, step.FadeOutSeconds));
    }
}
