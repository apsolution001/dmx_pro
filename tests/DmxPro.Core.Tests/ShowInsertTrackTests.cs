using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class ShowInsertTrackTests
{
    private readonly Project _project = new();
    private readonly SceneFunction _red;
    private readonly Show _show;

    public ShowInsertTrackTests()
    {
        var fixture = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
        _red = _project.CreateScene("Red");
        _red.SetValue(fixture.Id, "r", 255);
        _show = _project.CreateShow("Wedding");
    }

    [Fact]
    public void InsertTrack_IndexZero_InsertsAtTop()
    {
        var first = _show.Tracks[0];

        var track = _project.InsertTrack(_show.Id, 0, "Intro");

        Assert.Equal([track, first], _show.Tracks);
        Assert.Equal("Intro", track.Name);
        Assert.Empty(track.Items);
    }

    [Fact]
    public void InsertTrack_IndexBetween_InsertsBetweenAndKeepsOrder()
    {
        var a = _show.Tracks[0];
        var b = _project.AddTrack(_show.Id, "B");
        var c = _project.AddTrack(_show.Id, "C");

        var inserted = _project.InsertTrack(_show.Id, 2, "Between");

        Assert.Equal([a, b, inserted, c], _show.Tracks);
    }

    [Fact]
    public void InsertTrack_IndexEqualsCount_AppendsAtEnd()
    {
        var first = _show.Tracks[0];

        var track = _project.InsertTrack(_show.Id, _show.Tracks.Count, "Last");

        Assert.Equal([first, track], _show.Tracks);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void InsertTrack_IndexOutOfRange_ThrowsAndChangesNothing(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _project.InsertTrack(_show.Id, index, "X"));

        Assert.Single(_show.Tracks);
    }

    [Fact]
    public void InsertTrack_UnknownShow_Throws()
    {
        Assert.Throws<ArgumentException>(() => _project.InsertTrack(Guid.NewGuid(), 0, "X"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InsertTrack_BlankName_GetsNextFreeTrackName(string? name)
    {
        _project.AddTrack(_show.Id, "track 2");

        var track = _project.InsertTrack(_show.Id, 0, name);

        // "Track 1" is the default track; "track 2" is taken case-insensitively.
        Assert.Equal("Track 3", track.Name);
    }

    [Fact]
    public void InsertTrack_Name_IsTrimmed()
    {
        Assert.Equal("Front", _project.InsertTrack(_show.Id, 1, "  Front ").Name);
    }

    [Fact]
    public void InsertTrack_ExistingItems_PlaybackDataUnchanged()
    {
        var item = _project.PlaceFunctionInShow(_show.Id, _show.Tracks[0].Id, _red.Id, 2, 4);
        var functionsBefore = _project.Functions.Select(f => f.Id).ToList();

        _project.InsertTrack(_show.Id, 0, "Above");

        var track = _show.Tracks[1];
        var kept = Assert.Single(track.Items);
        Assert.Same(item, kept);
        Assert.Equal(2, kept.StartSeconds);
        Assert.Equal(4, kept.LengthSeconds);
        Assert.Equal(functionsBefore, _project.Functions.Select(f => f.Id));
        Assert.Same(track, _show.FindTrackOfItem(item.Id));
        Assert.Empty(_project.ValidateFunctionsAndShows());
    }

    [Fact]
    public void NextTrackName_NoTracks_ReturnsTrack1()
    {
        Assert.Equal("Track 1", Project.NextTrackName(new Show()));
    }
}
