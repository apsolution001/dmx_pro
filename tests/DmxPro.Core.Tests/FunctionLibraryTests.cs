using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class FunctionLibraryTests
{
    private readonly Project _project = new();
    private readonly Fixture _par;

    public FunctionLibraryTests()
    {
        _par = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
    }

    // ───────────────────────────── Library ─────────────────────────────

    [Fact]
    public void CreateScene_NoName_AddsLibrarySceneWithNextFreeName()
    {
        var first = _project.CreateScene();
        var second = _project.CreateScene();

        Assert.Equal("Scene 1", first.Name);
        Assert.Equal("Scene 2", second.Name);
        Assert.Null(first.OwnerShowId);
        Assert.Contains(first, _project.FunctionListItems);
        Assert.Equal(2, _project.Functions.Count);
    }

    [Fact]
    public void CreateScene_Name_TrimsName()
    {
        Assert.Equal("Red", _project.CreateScene("  Red ").Name);
    }

    [Fact]
    public void NextFunctionName_NameTakenCaseInsensitive_SkipsIt()
    {
        _project.CreateScene("scene 1");

        Assert.Equal("Scene 2", _project.NextFunctionName("Scene"));
    }

    [Fact]
    public void DuplicateFunction_Scene_InsertsIndependentCopyAfterOriginal()
    {
        var red = _project.CreateScene("Red");
        var other = _project.CreateScene("Other");
        red.SetValue(_par.Id, "r", 255);
        red.FadeInSeconds = 1.5;

        var copy = Assert.IsType<SceneFunction>(_project.DuplicateFunction(red.Id));
        copy.SetValue(_par.Id, "r", 10);

        Assert.Equal("Red copy", copy.Name);
        Assert.Equal(1, _project.Functions.IndexOf(copy));
        Assert.Equal(2, _project.Functions.IndexOf(other));
        Assert.Equal(1.5, copy.FadeInSeconds);
        Assert.Equal((byte)255, red.GetValue(_par.Id, "r"));
        Assert.Equal(red.Id, copy.CopiedFromId);
    }

    [Fact]
    public void DuplicateFunction_Twice_NumbersTheCopies()
    {
        var red = _project.CreateScene("Red");
        _project.DuplicateFunction(red.Id);

        Assert.Equal("Red copy 2", _project.DuplicateFunction(red.Id).Name);
    }

    [Fact]
    public void DuplicateFunction_ShowCopy_CopyIsLibraryFunction()
    {
        var red = _project.CreateScene("Red");
        var show = _project.CreateShow("Wedding");
        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, red.Id, 0, 4);

        var copy = _project.DuplicateFunction(item.FunctionId);

        Assert.Null(copy.OwnerShowId);
        Assert.True(copy.ShowInFunctionList);
    }

    [Fact]
    public void DuplicateFunction_UnknownId_Throws()
    {
        Assert.Throws<ArgumentException>(() => _project.DuplicateFunction(Guid.NewGuid()));
    }

    [Fact]
    public void RenameFunction_ValidName_TrimsAndRenames()
    {
        var scene = _project.CreateScene("Red");

        Assert.True(_project.RenameFunction(scene.Id, "  Deep red "));
        Assert.Equal("Deep red", scene.Name);
    }

    [Fact]
    public void RenameFunction_BlankName_KeepsOldName()
    {
        var scene = _project.CreateScene("Red");

        Assert.False(_project.RenameFunction(scene.Id, "  "));
        Assert.Equal("Red", scene.Name);
    }

    [Fact]
    public void DeleteFunction_LibraryFunctionPlacedInShow_DeletesItAndShowKeepsItsCopy()
    {
        var red = _project.CreateScene("Red");
        var show = _project.CreateShow("Wedding");
        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, red.Id, 0, 4);

        Assert.Equal(FunctionDeleteResult.Deleted, _project.DeleteFunction(red.Id));

        Assert.Null(_project.FindFunction(red.Id));
        Assert.NotNull(_project.FindFunction(item.FunctionId));
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void DeleteFunction_ListedShowCopyStillUsed_OnlyHidesIt()
    {
        var red = _project.CreateScene("Red");
        var show = _project.CreateShow("Wedding");
        var item = _project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, red.Id, 0, 4);
        _project.SetShowInFunctionList(item.FunctionId, true);

        Assert.Equal(FunctionDeleteResult.HiddenFromList, _project.DeleteFunction(item.FunctionId));

        var copy = _project.FindFunction(item.FunctionId);
        Assert.NotNull(copy);
        Assert.False(copy.ShowInFunctionList);
        Assert.DoesNotContain(copy, _project.FunctionListItems);
    }

    [Fact]
    public void DeleteFunction_UnknownId_ReturnsNotFound()
    {
        Assert.Equal(FunctionDeleteResult.NotFound, _project.DeleteFunction(Guid.NewGuid()));
    }

    // ───────────────────────────── Scene editing ─────────────────────────────

    [Fact]
    public void AddFixture_NewTarget_StoresEveryModeChannelAtProfileDefault()
    {
        var scene = _project.CreateScene();
        scene.SetValue(_par.Id, "r", 200);

        scene.AddFixture(_project, _par);

        Assert.Equal(8, scene.Values.Count);
        Assert.Equal((byte)200, scene.GetValue(_par.Id, "r"));
        Assert.Equal((byte)255, scene.GetValue(_par.Id, "dim"));
        Assert.Equal((byte)0, scene.GetValue(_par.Id, "strobe"));
        Assert.Equal([_par.Id], scene.TargetFixtureIds());
    }

    [Fact]
    public void RemoveFixture_Target_RemovesAllItsValues()
    {
        var scene = _project.CreateScene();
        scene.AddFixture(_project, _par);

        Assert.True(scene.RemoveFixture(_par.Id));
        Assert.Empty(scene.Values);
        Assert.False(scene.RemoveFixture(_par.Id));
    }

    [Fact]
    public void SetChannelType_ChannelOrderDiffers_ResolvesByTypeNotOffset()
    {
        var bar = _project.AddFixtures(TwoCellBar(), "dim-first", 1, 1, 20, "Bar")[0];
        var scene = _project.CreateScene();

        var count = scene.SetChannelType(_project, bar, ChannelType.Red, 180);

        Assert.Equal(2, count);
        Assert.Equal((byte)180, scene.GetValue(bar.Id, "r1"));
        Assert.Equal((byte)180, scene.GetValue(bar.Id, "r2"));
        Assert.Null(scene.GetValue(bar.Id, "dim"));
    }

    [Fact]
    public void SetChannelType_ModeWithoutType_SetsNothing()
    {
        var fourChannel = _project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 30, "Small")[0];
        var scene = _project.CreateScene();

        Assert.Equal(0, scene.SetChannelType(_project, fourChannel, ChannelType.Dimmer, 100));
        Assert.Empty(scene.Values);
        Assert.False(_project.HasChannelType(fourChannel, ChannelType.Dimmer));
        Assert.True(_project.HasChannelType(fourChannel, ChannelType.White));
    }

    [Fact]
    public void GetChannelTypeValue_NotStored_FallsBackToProfileDefault()
    {
        var scene = _project.CreateScene();

        Assert.Equal((byte)255, scene.GetChannelTypeValue(_project, _par, ChannelType.Dimmer));
        Assert.Equal((byte)0, scene.GetChannelTypeValue(_project, _par, ChannelType.Red));
        Assert.Null(scene.GetChannelTypeValue(_project, _par, ChannelType.UV));
    }

    [Fact]
    public void SetChannelIncluded_False_RemovesOnlyThatValue()
    {
        var scene = _project.CreateScene();
        scene.AddFixture(_project, _par);
        var strobe = _project.ResolveFixtureChannels(_par).First(c => c.Type == ChannelType.Strobe);

        scene.SetChannelIncluded(_par.Id, strobe, included: false);

        Assert.False(scene.IsChannelIncluded(_par.Id, "strobe"));
        Assert.True(scene.IsChannelIncluded(_par.Id, "r"));
        Assert.Equal(7, scene.Values.Count);
    }

    [Fact]
    public void SetChannelIncluded_TrueWithoutValue_StoresProfileDefault()
    {
        var scene = _project.CreateScene();
        var dimmer = _project.ResolveFixtureChannels(_par).First(c => c.Type == ChannelType.Dimmer);

        scene.SetChannelIncluded(_par.Id, dimmer, included: true);

        Assert.Equal((byte)255, scene.GetValue(_par.Id, "dim"));
    }

    [Fact]
    public void SetChannelIncluded_TrueWithValue_StoresValue()
    {
        var scene = _project.CreateScene();
        scene.SetValue(_par.Id, "dim", 10);
        var dimmer = _project.ResolveFixtureChannels(_par).First(c => c.Type == ChannelType.Dimmer);

        scene.SetChannelIncluded(_par.Id, dimmer, included: true, value: 99);

        Assert.Equal((byte)99, scene.GetValue(_par.Id, "dim"));
    }

    [Fact]
    public void SetIncludedChannelType_SomeChannelsExcluded_SetsOnlyIncluded()
    {
        var bar = _project.AddFixtures(TwoCellBar(), "dim-first", 1, 1, 20, "Bar")[0];
        var scene = _project.CreateScene();
        scene.SetValue(bar.Id, "r2", 5);

        var count = scene.SetIncludedChannelType(_project, bar, ChannelType.Red, 180);

        Assert.Equal(1, count);
        Assert.Null(scene.GetValue(bar.Id, "r1"));
        Assert.Equal((byte)180, scene.GetValue(bar.Id, "r2"));
    }

    [Fact]
    public void GetIncludedChannelTypeValue_FirstExcluded_ReturnsFirstIncluded()
    {
        var bar = _project.AddFixtures(TwoCellBar(), "dim-first", 1, 1, 20, "Bar")[0];
        var scene = _project.CreateScene();
        scene.SetValue(bar.Id, "r2", 77);

        Assert.Equal((byte)77, scene.GetIncludedChannelTypeValue(_project, bar, ChannelType.Red));
        Assert.Null(scene.GetIncludedChannelTypeValue(_project, bar, ChannelType.Dimmer));
    }

    [Theory]
    [InlineData(-5.0, 0)]
    [InlineData(127.5, 128)]
    [InlineData(254.4, 254)]
    [InlineData(300.0, 255)]
    [InlineData(double.NaN, 0)]
    public void ToDmxValue_AnyInput_RoundsAndClamps(double input, int expected)
    {
        Assert.Equal((byte)expected, SceneEditing.ToDmxValue(input));
    }

    [Fact]
    public void ResolveFixtureChannels_MissingProfile_ReturnsEmpty()
    {
        var orphan = new Fixture { ProfileId = "nope", ModeName = "x" };

        Assert.Empty(_project.ResolveFixtureChannels(orphan));
    }

    [Fact]
    public void PreviewColor_RedAtHalfDimmer_IsHalfRed()
    {
        var scene = _project.CreateScene();
        scene.SetValue(_par.Id, "r", 255);
        scene.SetValue(_par.Id, "dim", 128);

        Assert.Equal(((byte)128, (byte)0, (byte)0), scene.PreviewColor(_project, _par));
    }

    [Fact]
    public void PreviewColor_WhiteChannel_AddsToAllAndClamps()
    {
        var scene = _project.CreateScene();
        scene.SetValue(_par.Id, "r", 255);
        scene.SetValue(_par.Id, "w", 100);

        Assert.Equal(((byte)255, (byte)100, (byte)100), scene.PreviewColor(_project, _par));
    }

    private static FixtureProfile TwoCellBar() => new()
    {
        Id = "test-bar",
        Model = "Bar",
        Channels =
        [
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer, Default = 255 },
            new() { Key = "r1", Name = "Red 1", Type = ChannelType.Red },
            new() { Key = "r2", Name = "Red 2", Type = ChannelType.Red },
        ],
        Modes = [new() { Name = "dim-first", Channels = ["dim", "r1", "r2"] }],
    };
}
