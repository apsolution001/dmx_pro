using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class ShowEditingTests
{
    private readonly Project _project = new();
    private readonly Fixture _fixture;
    private readonly SceneFunction _red;
    private readonly Show _show;

    public ShowEditingTests()
    {
        _fixture = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
        _red = new SceneFunction { Name = "Red", FadeInSeconds = 0.5 };
        _red.SetValue(_fixture.Id, "r", 255);
        _project.Functions.Add(_red);
        _show = _project.CreateShow("Wedding");
    }

    private Guid TrackId => _show.Tracks[0].Id;

    private SceneFunction FunctionOf(ShowItem item) => Assert.IsType<SceneFunction>(_project.FindFunction(item.FunctionId));

    [Fact]
    public void CreateShow_Name_HasDefaultsAndOneEmptyTrack()
    {
        Assert.Equal("Wedding", _show.Name);
        Assert.Equal(Show.DefaultLengthSeconds, _show.LengthSeconds);
        Assert.Equal(Show.DefaultBpm, _show.Bpm);
        Assert.Empty(Assert.Single(_show.Tracks).Items);
        Assert.Same(_show, _project.FindShow(_show.Id));
    }

    [Fact]
    public void AddTrack_ExistingShow_AppendsTrack()
    {
        var track = _project.AddTrack(_show.Id, "Back");

        Assert.Equal(2, _show.Tracks.Count);
        Assert.Same(track, _show.FindTrack(track.Id));
    }

    [Fact]
    public void PlaceFunctionInShow_LibraryFunction_CreatesIndependentShowOwnedCopy()
    {
        var item = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 2, 4);

        var copy = FunctionOf(item);
        Assert.NotEqual(_red.Id, copy.Id);
        Assert.Equal(_show.Id, copy.OwnerShowId);
        Assert.False(copy.ShowInFunctionList);
        Assert.Equal(_red.Id, copy.CopiedFromId);
        Assert.Equal("Red", copy.Name);
        Assert.Equal((byte)255, copy.GetValue(_fixture.Id, "r"));
        Assert.Equal(0.5, copy.FadeInSeconds);
        Assert.Equal(2, item.StartSeconds);
        Assert.Equal(4, item.LengthSeconds);
        Assert.Same(item, Assert.Single(_show.Tracks[0].Items));
    }

    [Fact]
    public void PlaceFunctionInShow_EditCopy_SourceUnchanged()
    {
        var copy = FunctionOf(_project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4));

        copy.SetValue(_fixture.Id, "r", 10);
        copy.SetValue(_fixture.Id, "g", 99);
        copy.Name = "Red centre";
        copy.FadeInSeconds = 2;

        Assert.Equal((byte)255, _red.GetValue(_fixture.Id, "r"));
        Assert.Null(_red.GetValue(_fixture.Id, "g"));
        Assert.Equal("Red", _red.Name);
        Assert.Equal(0.5, _red.FadeInSeconds);
        Assert.Single(_red.Values);
    }

    [Fact]
    public void PlaceFunctionInShow_UnknownFunction_Throws()
    {
        Assert.Throws<ArgumentException>(() => _project.PlaceFunctionInShow(_show.Id, TrackId, Guid.NewGuid(), 0, 1));
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(0, 0)]
    [InlineData(0, -2)]
    [InlineData(double.NaN, 4)]
    public void PlaceFunctionInShow_InvalidTiming_ThrowsAndAddsNothing(double start, double length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, start, length));

        Assert.Single(_project.Functions);
        Assert.Empty(_show.Tracks[0].Items);
    }

    [Fact]
    public void FunctionListItems_ShowCopy_HiddenUntilTicked()
    {
        var item = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4);

        Assert.Equal([_red.Id], _project.FunctionListItems.Select(f => f.Id));

        _project.SetShowInFunctionList(item.FunctionId, true);

        Assert.Equal([_red.Id, item.FunctionId], _project.FunctionListItems.Select(f => f.Id));
    }

    [Fact]
    public void FunctionListItems_LibraryFunctionWithFlagOff_StillListed()
    {
        _project.SetShowInFunctionList(_red.Id, false);

        Assert.Contains(_red, _project.FunctionListItems);
    }

    [Fact]
    public void CreateFunctionInShow_Scene_CreatesHiddenShowOwnedScene()
    {
        var item = _project.CreateFunctionInShow(_show.Id, TrackId, FunctionType.Scene, "Blue wash", 8, 2);

        var scene = FunctionOf(item);
        Assert.Equal("Blue wash", scene.Name);
        Assert.Equal(_show.Id, scene.OwnerShowId);
        Assert.False(scene.ShowInFunctionList);
        Assert.Null(scene.CopiedFromId);
        Assert.Empty(scene.Values);
        Assert.DoesNotContain(scene, _project.FunctionListItems);
    }

    [Fact]
    public void CreateFunctionInShow_AnyType_CreatesHiddenShowCopyOfThatType()
    {
        foreach (var type in Enum.GetValues<FunctionType>())
        {
            var item = _project.CreateFunctionInShow(_show.Id, TrackId, type, $"New {type}", 0, 4);

            var function = _project.FindFunction(item.FunctionId);
            Assert.NotNull(function);
            // Only Scene and Chaser remain (2026-10-28): every other (old) enum value creates a chaser.
            Assert.Equal(type == FunctionType.Scene ? FunctionType.Scene : FunctionType.Chaser, function.Type);
            Assert.Equal($"New {type}", function.Name);
            Assert.Equal(_show.Id, function.OwnerShowId);
            Assert.False(function.ShowInFunctionList);
            Assert.Contains(item, _show.Tracks[0].Items);
        }

        Assert.Equal(1 + Enum.GetValues<FunctionType>().Length, _project.Functions.Count);
    }

    [Fact]
    public void DuplicateShowItem_Item_PlacesAfterWithIndependentFunctionCopy()
    {
        var original = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 2, 4);
        original.SetTiming(2, 4, 1, 1);

        var duplicate = _project.DuplicateShowItem(_show.Id, original.Id);

        Assert.NotEqual(original.FunctionId, duplicate.FunctionId);
        Assert.Equal(6, duplicate.StartSeconds);
        Assert.Equal(4, duplicate.LengthSeconds);
        Assert.Equal(1, duplicate.FadeInSeconds);
        Assert.Equal([original.Id, duplicate.Id], _show.Tracks[0].Items.Select(i => i.Id));

        var duplicateFunction = FunctionOf(duplicate);
        Assert.Equal(_show.Id, duplicateFunction.OwnerShowId);
        Assert.False(duplicateFunction.ShowInFunctionList);
        duplicateFunction.SetValue(_fixture.Id, "r", 1);
        Assert.Equal((byte)255, FunctionOf(original).GetValue(_fixture.Id, "r"));
    }

    [Fact]
    public void RemoveShowItem_UnlistedCopy_DeletesCopy()
    {
        var item = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4);

        Assert.True(_project.RemoveShowItem(_show.Id, item.Id));

        Assert.Empty(_show.Tracks[0].Items);
        Assert.Null(_project.FindFunction(item.FunctionId));
        Assert.NotNull(_project.FindFunction(_red.Id));
    }

    [Fact]
    public void RemoveShowItem_ListedCopy_KeepsCopy()
    {
        var item = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4);
        _project.SetShowInFunctionList(item.FunctionId, true);

        _project.RemoveShowItem(_show.Id, item.Id);

        Assert.NotNull(_project.FindFunction(item.FunctionId));
    }

    [Fact]
    public void RemoveShowItem_UnknownItem_ReturnsFalse()
    {
        Assert.False(_project.RemoveShowItem(_show.Id, Guid.NewGuid()));
    }

    [Fact]
    public void RemoveTrack_WithItems_RemovesTrackAndUnlistedCopies()
    {
        var track = _project.AddTrack(_show.Id, "Back");
        var item = _project.PlaceFunctionInShow(_show.Id, track.Id, _red.Id, 0, 4);

        Assert.True(_project.RemoveTrack(_show.Id, track.Id));

        Assert.Null(_show.FindTrack(track.Id));
        Assert.Null(_project.FindFunction(item.FunctionId));
    }

    [Fact]
    public void MoveShowItem_OtherTrack_MovesAndRetimes()
    {
        var target = _project.AddTrack(_show.Id, "Back");
        var item = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4);

        _project.MoveShowItem(_show.Id, item.Id, target.Id, 10, 2);

        Assert.Empty(_show.Tracks[0].Items);
        Assert.Same(item, Assert.Single(target.Items));
        Assert.Equal(10, item.StartSeconds);
        Assert.Equal(2, item.LengthSeconds);
    }

    [Fact]
    public void DeleteShow_WithCopies_RemovesUnlistedAndPromotesListedToLibrary()
    {
        var hidden = _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4);
        var listed = _project.CreateFunctionInShow(_show.Id, TrackId, FunctionType.Scene, "Keep me", 4, 4);
        _project.SetShowInFunctionList(listed.FunctionId, true);

        Assert.True(_project.DeleteShow(_show.Id));

        Assert.Empty(_project.Shows);
        Assert.Null(_project.FindFunction(hidden.FunctionId));
        var kept = _project.FindFunction(listed.FunctionId);
        Assert.NotNull(kept);
        Assert.Null(kept.OwnerShowId);
        Assert.Contains(kept, _project.FunctionListItems);
        Assert.NotNull(_project.FindFunction(_red.Id));
    }

    [Fact]
    public void DeleteShow_UnknownShow_ReturnsFalse()
    {
        Assert.False(_project.DeleteShow(Guid.NewGuid()));
    }

    [Fact]
    public void SetTiming_FadesTooLong_ClampsFadeOutFirstThenFadeIn()
    {
        var item = new ShowItem();

        item.SetTiming(0, 4, fadeInSeconds: 3, fadeOutSeconds: 3);
        Assert.Equal(3, item.FadeInSeconds);
        Assert.Equal(1, item.FadeOutSeconds);

        item.SetTiming(0, 4, fadeInSeconds: 9, fadeOutSeconds: -1);
        Assert.Equal(4, item.FadeInSeconds);
        Assert.Equal(0, item.FadeOutSeconds);
        Assert.True(item.HasValidTiming);
    }

    [Fact]
    public void Validate_ItemWithMissingFunction_ReportsWarning()
    {
        _show.Tracks[0].Items.Add(new ShowItem { FunctionId = Guid.NewGuid(), LengthSeconds = 1 });

        Assert.Contains(_project.Validate(), i => i.Code == "SHOW_ITEM_MISSING_FUNCTION");
    }

    [Fact]
    public void Validate_ItemWithInvalidTiming_ReportsWarning()
    {
        _show.Tracks[0].Items.Add(new ShowItem { FunctionId = _red.Id, LengthSeconds = 1, FadeInSeconds = 2 });

        Assert.Contains(_project.Validate(), i => i.Code == "SHOW_ITEM_INVALID_TIMING");
    }

    [Fact]
    public void Validate_SceneValueForMissingFixture_ReportsWarning()
    {
        _red.SetValue(Guid.NewGuid(), "r", 1);

        Assert.Contains(_project.Validate(), i => i.Code == "SCENE_MISSING_FIXTURE" && i.EntityId == _red.Id);
    }

    [Fact]
    public void Validate_SceneChannelKeyNotInMode_ReportsWarning()
    {
        var small = _project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 20, "Small")[0];
        _red.SetValue(small.Id, "dim", 255);

        Assert.Contains(_project.Validate(), i => i.Code == "SCENE_UNKNOWN_CHANNEL" && i.EntityId == _red.Id);
    }

    [Fact]
    public void Validate_ConsistentShow_ReportsNoShowIssues()
    {
        _project.PlaceFunctionInShow(_show.Id, TrackId, _red.Id, 0, 4);

        Assert.Empty(_project.ValidateFunctionsAndShows());
    }
}
