using DmxPro.Core.Functions;
using DmxPro.Engine.Runtime;

namespace DmxPro.Engine.Tests;

public class CompileSharingTests
{
    [Fact]
    public void Intern_EqualContent_ReturnsSameArray()
    {
        var interner = new ArrayInterner<int>();

        var first = interner.Intern([1, 2, 3]);
        var second = interner.Intern([1, 2, 3]);
        var other = interner.Intern([1, 2, 4]);

        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal([1, 2, 3], first);
        Assert.Equal([1, 2, 4], other);
    }

    [Fact]
    public void Intern_Empty_ReturnsEmptyArray()
    {
        var interner = new ArrayInterner<byte>();

        Assert.Empty(interner.Intern([]));
    }

    [Fact]
    public void Intern_CopiesContent_CallerBufferCanBeReused()
    {
        var interner = new ArrayInterner<byte>();
        var buffer = new byte[] { 7, 8 };

        var interned = interner.Intern(buffer);
        buffer[0] = 99;

        Assert.Equal([7, 8], interned);
        Assert.Same(interned, interner.Intern([7, 8]));
    }

    [Fact]
    public void FirstLiveIndex_LongItemBeforeShortOnes_KeepsLongItemLive()
    {
        var scene = new SceneProgram(Guid.NewGuid(), 0, 0, [], []);
        ShowItemProgram[] items =
        [
            new(scene, 0, 100, 0, 0),
            new(scene, 1, 2, 0, 0),
            new(scene, 3, 4, 0, 0),
            new(scene, 50, 60, 0, 0),
        ];
        var program = new ShowProgram(Guid.NewGuid(), 120, items);

        Assert.Equal(0, program.FirstLiveIndex(0));
        Assert.Equal(0, program.FirstLiveIndex(55));
        Assert.Equal(4, program.FirstLiveIndex(100));
    }

    [Fact]
    public void FirstLiveIndex_SequentialItems_SkipsEndedOnes()
    {
        var scene = new SceneProgram(Guid.NewGuid(), 0, 0, [], []);
        ShowItemProgram[] items = [new(scene, 0, 1, 0, 0), new(scene, 1, 2, 0, 0), new(scene, 2, 3, 0, 0)];
        var program = new ShowProgram(Guid.NewGuid(), 3, items);

        Assert.Equal(0, program.FirstLiveIndex(0.5));
        Assert.Equal(1, program.FirstLiveIndex(1));
        Assert.Equal(2, program.FirstLiveIndex(2.5));
        Assert.Equal(3, program.FirstLiveIndex(3));
    }

    [Fact]
    public void ShowProgram_UnsortedItems_Throws()
    {
        var scene = new SceneProgram(Guid.NewGuid(), 0, 0, [], []);

        Assert.Throws<ArgumentException>(() => new ShowProgram(Guid.NewGuid(), 10, [new(scene, 5, 6, 0, 0), new(scene, 1, 2, 0, 0)]));
    }

    [Fact]
    public void Compile_ColorCycleAndFlashWithEqualFixtureLists_KeepTheirOwnFixtureRules()
    {
        // The compiler shares resolved fixture lists by content; colour-only (cycle) and any-fixture (flash) must not
        // share one: the dimmer-only light takes part in the flash but not in the colour cycle.
        var rig = new TestRig();
        var dimmer = rig.AddDimmer(100);
        var cycle = rig.Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [rig.Fixture.Id, dimmer.Id], Colors = [new RgbColor(255, 0, 0)] });
        var flash = rig.Add(new FlashFunction { Name = "Flash", FixtureIds = [rig.Fixture.Id, dimmer.Id], Color = new RgbColor(255, 255, 255) });
        rig.Load();

        rig.Engine.StartFunction(cycle.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(100));

        rig.Engine.StopFunction(cycle.Id, EngineLayer.Console);
        rig.Engine.StartFunction(flash.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(255, rig.At(100));
    }

    [Fact]
    public void Compile_SceneCopiesWithEqualAndDifferentValues_EachPlaysItsOwnValues()
    {
        var rig = new TestRig();
        var original = rig.Scene("Original", ("r", 255), ("g", 10));
        var sameCopy = rig.Add((SceneFunction)original.CloneAsNew());
        var changedCopy = rig.Add((SceneFunction)original.CloneAsNew());
        changedCopy.SetValue(rig.Fixture.Id, "r", 100);
        rig.Load();

        rig.Engine.StartFunction(sameCopy.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(10, rig.At(TestRig.Green));

        rig.Engine.ReleaseAll();
        rig.Engine.StartFunction(changedCopy.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(100, rig.At(TestRig.Red));
        Assert.Equal(10, rig.At(TestRig.Green));
    }
}
