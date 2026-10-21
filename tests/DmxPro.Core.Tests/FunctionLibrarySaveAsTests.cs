using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

/// <summary>"Save as function" from a show block and from the Test &amp; Preview page.</summary>
public class FunctionLibrarySaveAsTests
{
    private readonly Project _project = new();
    private readonly Fixture _par;
    private readonly Fixture _par2;
    private readonly SceneFunction _red;
    private readonly Show _show;

    public FunctionLibrarySaveAsTests()
    {
        var fixtures = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "PAR");
        _par = fixtures[0];
        _par2 = fixtures[1];
        _red = _project.CreateScene("Red");
        _red.SetValue(_par.Id, "r", 255);
        _show = _project.CreateShow("Wedding");
    }

    private SceneFunction PlaceRedCopy()
    {
        var item = _project.PlaceFunctionInShow(_show.Id, _show.Tracks[0].Id, _red.Id, 0, 4);
        return Assert.IsType<SceneFunction>(_project.FindFunction(item.FunctionId));
    }

    // ───────────────────────────── Save a show copy to the library ─────────────────────────────

    [Fact]
    public void DuplicateFunction_ShowCopyWithName_CreatesLibraryFunctionWithThatName()
    {
        var showCopy = PlaceRedCopy();
        showCopy.SetValue(_par2.Id, "r", 255);

        var saved = Assert.IsType<SceneFunction>(_project.DuplicateFunction(showCopy.Id, "  All red "));

        Assert.Equal("All red", saved.Name);
        Assert.NotEqual(showCopy.Id, saved.Id);
        Assert.NotEqual(_red.Id, saved.Id);
        Assert.Null(saved.OwnerShowId);
        Assert.True(saved.ShowInFunctionList);
        Assert.Contains(saved, _project.FunctionListItems);
        Assert.Equal((byte)255, saved.GetValue(_par2.Id, "r"));
    }

    [Fact]
    public void DuplicateFunction_ShowCopy_IsDeepCopyAndShowKeepsItsCopy()
    {
        var showCopy = PlaceRedCopy();
        var item = _show.Tracks[0].Items[0];

        var saved = Assert.IsType<SceneFunction>(_project.DuplicateFunction(showCopy.Id, "Saved red"));
        saved.SetValue(_par.Id, "r", 10);
        saved.Values.Add(new SceneValue { FixtureId = _par2.Id, ChannelKey = "g", Value = 1 });

        Assert.Equal(showCopy.Id, item.FunctionId);
        Assert.Equal(_show.Id, showCopy.OwnerShowId);
        Assert.False(showCopy.ShowInFunctionList);
        Assert.Equal((byte)255, showCopy.GetValue(_par.Id, "r"));
        Assert.Single(showCopy.Values);
        Assert.Equal((byte)255, _red.GetValue(_par.Id, "r"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void DuplicateFunction_BlankName_FallsBackToCopyName(string? name)
    {
        Assert.Equal("Red copy", _project.DuplicateFunction(_red.Id, name).Name);
    }

    [Fact]
    public void SuggestFunctionName_NameFreeInList_ReturnsName()
    {
        var showCopy = PlaceRedCopy();
        _project.DeleteFunction(_red.Id);

        // The hidden show copy itself is called "Red" but is not in the Functions list.
        Assert.Equal("Red", _project.SuggestFunctionName(showCopy.Name));
    }

    [Fact]
    public void SuggestFunctionName_NameTaken_AppendsNextNumber()
    {
        _project.CreateScene("red 2");

        Assert.Equal("Red 3", _project.SuggestFunctionName(" Red "));
    }

    [Fact]
    public void SuggestFunctionName_Blank_ReturnsGenericName()
    {
        Assert.Equal("Function 1", _project.SuggestFunctionName("  "));
    }

    [Fact]
    public void IsFunctionNameTaken_ListedAndHiddenNames_OnlyListedCount()
    {
        var showCopy = PlaceRedCopy();
        showCopy.Name = "Show only";

        Assert.True(_project.IsFunctionNameTaken(" RED "));
        Assert.False(_project.IsFunctionNameTaken("Show only"));
        Assert.False(_project.IsFunctionNameTaken(" "));
    }

    // ───────────────────────────── Scene from test values ─────────────────────────────

    [Fact]
    public void CreateSceneFromValues_Values_CreatesLibrarySceneWithValuesAndFades()
    {
        var scene = _project.CreateSceneFromValues(
            "  Test look 1 ",
            [
                new SceneValue { FixtureId = _par.Id, ChannelKey = "r", Value = 200 },
                new SceneValue { FixtureId = _par2.Id, ChannelKey = "b", Value = 50 },
            ],
            fadeInSeconds: 1.5,
            fadeOutSeconds: 2);

        Assert.Equal("Test look 1", scene.Name);
        Assert.Null(scene.OwnerShowId);
        Assert.Contains(scene, _project.FunctionListItems);
        Assert.Equal(2, scene.Values.Count);
        Assert.Equal((byte)200, scene.GetValue(_par.Id, "r"));
        Assert.Equal((byte)50, scene.GetValue(_par2.Id, "b"));
        Assert.Equal(1.5, scene.FadeInSeconds);
        Assert.Equal(2, scene.FadeOutSeconds);
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void CreateSceneFromValues_UnknownFixtureOrChannel_SkipsThem()
    {
        var scene = _project.CreateSceneFromValues(
            "Look",
            [
                new SceneValue { FixtureId = Guid.NewGuid(), ChannelKey = "r", Value = 1 },
                new SceneValue { FixtureId = _par.Id, ChannelKey = "no-such-channel", Value = 2 },
                new SceneValue { FixtureId = _par.Id, ChannelKey = "g", Value = 3 },
            ]);

        var value = Assert.Single(scene.Values);
        Assert.Equal("g", value.ChannelKey);
        Assert.Equal((byte)3, value.Value);
    }

    [Fact]
    public void CreateSceneFromValues_SameChannelTwice_LastValueWins()
    {
        var scene = _project.CreateSceneFromValues(
            "Look",
            [
                new SceneValue { FixtureId = _par.Id, ChannelKey = "r", Value = 1 },
                new SceneValue { FixtureId = _par.Id, ChannelKey = "r", Value = 9 },
            ]);

        Assert.Equal((byte)9, Assert.Single(scene.Values).Value);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(99999, FunctionEditing.MaxSeconds)]
    public void CreateSceneFromValues_BadFade_IsClamped(double fade, double expected)
    {
        var scene = _project.CreateSceneFromValues("Look", [], fade, fade);

        Assert.Equal(expected, scene.FadeInSeconds);
        Assert.Equal(expected, scene.FadeOutSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSceneFromValues_BlankName_ThrowsAndAddsNothing(string name)
    {
        var before = _project.Functions.Count;

        Assert.Throws<ArgumentException>(() => _project.CreateSceneFromValues(name, []));
        Assert.Equal(before, _project.Functions.Count);
    }
}
