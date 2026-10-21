using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

/// <summary>Edge cases for FixtureSelection, show tracks, library save-as, clipboard, recent list and console buttons.</summary>
public class TodaysCoreEdgeTests
{
    private static Guid[] Ids(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();

    // ───────────────────────────── FixtureSelection ─────────────────────────────

    [Fact]
    public void Merge_AllEmpty_ReturnsEmpty()
    {
        Assert.Empty(FixtureSelection.Merge([], [], new HashSet<Guid>()));
    }

    [Fact]
    public void Merge_DuplicatesInPatchOrder_PickedAddedOnce()
    {
        var a = Ids(2);
        var result = FixtureSelection.Merge([], [a[0], a[1], a[0]], new HashSet<Guid>([a[0], a[1]]));

        Assert.Equal([a[0], a[1]], result);
    }

    [Fact]
    public void Merge_ExistingIdMissingFromPatchAndNotPicked_IsKept()
    {
        var a = Ids(2);
        var deleted = Guid.NewGuid();

        var result = FixtureSelection.Merge([deleted, a[0]], [a[0], a[1]], new HashSet<Guid>([a[1]]));

        Assert.Equal([deleted, a[1]], result);
    }

    [Fact]
    public void Merge_DuplicateOfMissingExistingId_KeptOnce()
    {
        var gone = Guid.NewGuid();

        var result = FixtureSelection.Merge([gone, gone], [], new HashSet<Guid>());

        Assert.Equal([gone], result);
    }

    [Fact]
    public void Merge_ExistingPickedAgain_NotAppendedTwice()
    {
        var a = Ids(3);

        var result = FixtureSelection.Merge([a[2]], [a[0], a[1], a[2]], new HashSet<Guid>(a));

        Assert.Equal([a[2], a[0], a[1]], result);
    }

    [Fact]
    public void Merge_ResultIsIndependentOfInputs()
    {
        var a = Ids(2);
        var existing = new List<Guid> { a[0] };

        var result = FixtureSelection.Merge(existing, a, new HashSet<Guid>(a));
        existing.Clear();

        Assert.Equal(a, result);
    }

    [Fact]
    public void GroupState_DuplicateMembersAllPicked_ReturnsTrue()
    {
        var a = Ids(1);

        Assert.True(FixtureSelection.GroupState([a[0], a[0]], new HashSet<Guid>(a)));
    }

    [Fact]
    public void Added_EmptyBefore_ReturnsAllAfterDeduplicated()
    {
        var a = Ids(2);

        Assert.Equal(a, FixtureSelection.Added([], [a[0], a[1], a[0]]));
    }

    // ───────────────────────────── InsertTrack ─────────────────────────────

    [Fact]
    public void InsertTrack_EmptyShowWithNoTracks_IndexZeroWorks()
    {
        var project = new Project();
        var show = project.CreateShow("S");
        show.Tracks.Clear();

        var track = project.InsertTrack(show.Id, 0);

        Assert.Equal("Track 1", track.Name);
        Assert.Single(show.Tracks);
    }

    [Fact]
    public void InsertTrack_NameCollidesCaseInsensitive_BlankNameSkipsIt()
    {
        var project = new Project();
        var show = project.CreateShow("S");
        show.Tracks[0].Name = "TRACK 1";

        var track = project.InsertTrack(show.Id, 1);

        Assert.Equal("Track 2", track.Name);
    }

    [Fact]
    public void InsertTrack_ExplicitDuplicateName_IsAllowed()
    {
        var project = new Project();
        var show = project.CreateShow("S");

        var track = project.InsertTrack(show.Id, 0, "Track 1");

        Assert.Equal("Track 1", track.Name);
        Assert.Equal(2, show.Tracks.Count(t => t.Name == "Track 1"));
    }

    [Fact]
    public void InsertTrack_TrackNamesWithGaps_FillsFirstGap()
    {
        var project = new Project();
        var show = project.CreateShow("S");
        show.Tracks[0].Name = "Track 2";
        project.AddTrack(show.Id, "Track 3");

        Assert.Equal("Track 1", project.InsertTrack(show.Id, 0).Name);
        Assert.Equal("Track 4", project.InsertTrack(show.Id, 0).Name);
    }

    [Fact]
    public void InsertTrack_NegativeIndexOrBeyondCount_ThrowsAndLeavesTracks()
    {
        var project = new Project();
        var show = project.CreateShow("S");

        Assert.Throws<ArgumentOutOfRangeException>(() => project.InsertTrack(show.Id, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.InsertTrack(show.Id, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.InsertTrack(show.Id, int.MaxValue));
        Assert.Single(show.Tracks);
    }

    [Fact]
    public void InsertTrack_WhitespaceOnlyName_GetsGeneratedName()
    {
        var project = new Project();
        var show = project.CreateShow("S");

        Assert.Equal("Track 2", project.InsertTrack(show.Id, 1, "   \t").Name);
    }

    // ───────────────────────────── CreateSceneFromValues ─────────────────────────────

    private static (Project Project, Fixture Fixture) ProjectWithPar()
    {
        var project = new Project();
        var fixture = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "PAR")[0];
        return (project, fixture);
    }

    [Fact]
    public void CreateSceneFromValues_NoValues_CreatesEmptyScene()
    {
        var (project, _) = ProjectWithPar();

        var scene = project.CreateSceneFromValues("Empty", []);

        Assert.Empty(scene.Values);
        Assert.Same(scene, project.FindFunction(scene.Id));
    }

    [Fact]
    public void CreateSceneFromValues_ChannelKeyOfOtherModeOnly_IsSkipped()
    {
        var (project, fixture) = ProjectWithPar(); // 4ch mode has no "dim"

        var scene = project.CreateSceneFromValues("S", [new SceneValue { FixtureId = fixture.Id, ChannelKey = "dim", Value = 200 }]);

        Assert.Empty(scene.Values);
    }

    [Fact]
    public void CreateSceneFromValues_ChannelKeyDifferentCase_IsSkippedBecauseKeysAreOrdinal()
    {
        var (project, fixture) = ProjectWithPar();

        var scene = project.CreateSceneFromValues("S", [new SceneValue { FixtureId = fixture.Id, ChannelKey = "R", Value = 200 }]);

        Assert.Empty(scene.Values);
    }

    [Fact]
    public void CreateSceneFromValues_BlankChannelKey_IsSkipped()
    {
        var (project, fixture) = ProjectWithPar();

        var scene = project.CreateSceneFromValues("S", [new SceneValue { FixtureId = fixture.Id, ChannelKey = " ", Value = 1 }]);

        Assert.Empty(scene.Values);
    }

    [Fact]
    public void CreateSceneFromValues_NullEntry_IsSkipped()
    {
        var (project, fixture) = ProjectWithPar();

        var scene = project.CreateSceneFromValues("S", [null!, new SceneValue { FixtureId = fixture.Id, ChannelKey = "r", Value = 9 }]);

        Assert.Single(scene.Values);
    }

    [Fact]
    public void CreateSceneFromValues_FixtureWithMissingProfile_KeepsValueBecauseKeyCannotBeChecked()
    {
        var project = new Project();
        var fixture = new Fixture { Name = "orphan", ProfileId = "nope", ModeName = "x" };
        project.Fixtures.Add(fixture);

        var scene = project.CreateSceneFromValues("S", [new SceneValue { FixtureId = fixture.Id, ChannelKey = "r", Value = 5 }]);

        Assert.Single(scene.Values);
    }

    [Fact]
    public void CreateSceneFromValues_ByteExtremes_Stored0And255()
    {
        var (project, fixture) = ProjectWithPar();

        var scene = project.CreateSceneFromValues(
            "S",
            [
                new SceneValue { FixtureId = fixture.Id, ChannelKey = "r", Value = 0 },
                new SceneValue { FixtureId = fixture.Id, ChannelKey = "g", Value = 255 },
            ]);

        Assert.Equal(0, scene.Values.Single(v => v.ChannelKey == "r").Value);
        Assert.Equal(255, scene.Values.Single(v => v.ChannelKey == "g").Value);
    }

    [Fact]
    public void CreateSceneFromValues_NanAndInfiniteFades_AreSafe()
    {
        var (project, _) = ProjectWithPar();

        var nan = project.CreateSceneFromValues("nan", [], double.NaN, double.NaN);
        var inf = project.CreateSceneFromValues("inf", [], double.PositiveInfinity, double.NegativeInfinity);

        Assert.Equal(0, nan.FadeInSeconds);
        Assert.Equal(0, nan.FadeOutSeconds);
        Assert.Equal(FunctionEditing.MaxSeconds, inf.FadeInSeconds);
        Assert.Equal(0, inf.FadeOutSeconds);
    }

    [Fact]
    public void CreateSceneFromValues_NameIsTrimmed()
    {
        var (project, _) = ProjectWithPar();

        Assert.Equal("Look", project.CreateSceneFromValues("  Look  ", []).Name);
    }

    [Fact]
    public void CreateSceneFromValues_NullName_Throws()
    {
        var (project, _) = ProjectWithPar();

        Assert.Throws<ArgumentNullException>(() => project.CreateSceneFromValues(null!, []));
    }

    [Fact]
    public void CreateSceneFromValues_NullValues_Throws()
    {
        var (project, _) = ProjectWithPar();

        Assert.Throws<ArgumentNullException>(() => project.CreateSceneFromValues("S", null!));
    }

    // ───────────────────────────── DuplicateFunction / names ─────────────────────────────

    [Fact]
    public void DuplicateFunction_NameWithSpaces_IsTrimmed()
    {
        var project = new Project();
        var scene = project.CreateScene("Red");

        var copy = project.DuplicateFunction(scene.Id, "  Warm  ");

        Assert.Equal("Warm", copy.Name);
    }

    [Fact]
    public void DuplicateFunction_NameSameAsExisting_IsNotMadeUnique()
    {
        var project = new Project();
        var scene = project.CreateScene("Red");

        var copy = project.DuplicateFunction(scene.Id, "Red");

        Assert.Equal("Red", copy.Name);
        Assert.NotEqual(scene.Id, copy.Id);
    }

    [Fact]
    public void DuplicateFunction_UnknownId_Throws()
    {
        var project = new Project();

        Assert.ThrowsAny<Exception>(() => project.DuplicateFunction(Guid.NewGuid(), "x"));
    }

    [Fact]
    public void SuggestFunctionName_NullName_ReturnsGenericName()
    {
        var project = new Project();

        Assert.False(string.IsNullOrWhiteSpace(project.SuggestFunctionName(null!)));
    }

    [Fact]
    public void SuggestFunctionName_TrimmedNameTakenCaseInsensitive_AppendsTwo()
    {
        var project = new Project();
        project.CreateScene("Red");

        Assert.Equal("RED 2", project.SuggestFunctionName("  RED "));
    }

    [Fact]
    public void SuggestFunctionName_NumberedNameAlsoTaken_SkipsToNextFree()
    {
        var project = new Project();
        project.CreateScene("Red");
        project.CreateScene("Red 2");

        Assert.Equal("Red 3", project.SuggestFunctionName("Red"));
    }

    [Fact]
    public void IsFunctionNameTaken_NullOrBlank_IsFalse()
    {
        var project = new Project();
        project.CreateScene("Red");

        Assert.False(project.IsFunctionNameTaken(null!));
        Assert.False(project.IsFunctionNameTaken("  "));
        Assert.True(project.IsFunctionNameTaken(" red "));
    }

    // ───────────────────────────── FunctionClipboard ─────────────────────────────

    [Fact]
    public void PasteInto_OriginalRenamedAndValuesMutatedAfterCopy_PastesOriginalSnapshot()
    {
        var project = new Project();
        var clipboard = new FunctionClipboard();
        var fixtureId = Guid.NewGuid();
        var scene = project.CreateScene("Red");
        scene.SetValue(fixtureId, "r", 255);
        clipboard.Copy(scene);

        scene.Name = "Changed";
        scene.SetValue(fixtureId, "r", 1);
        scene.SetValue(fixtureId, "g", 2);

        var pasted = Assert.IsType<SceneFunction>(clipboard.PasteInto(project));
        Assert.Equal("Red (copy)", pasted.Name);
        Assert.Equal("Red", clipboard.FunctionName);
        var value = Assert.Single(pasted.Values);
        Assert.Equal(255, value.Value);
    }

    [Fact]
    public void PasteInto_PastedCopyMutated_SecondPasteStillHasSnapshotValues()
    {
        var project = new Project();
        var clipboard = new FunctionClipboard();
        var fixtureId = Guid.NewGuid();
        var scene = project.CreateScene("Red");
        scene.SetValue(fixtureId, "r", 255);
        clipboard.Copy(scene);

        var first = Assert.IsType<SceneFunction>(clipboard.PasteInto(project));
        first.SetValue(fixtureId, "r", 3);
        var second = Assert.IsType<SceneFunction>(clipboard.PasteInto(project));

        Assert.Equal(255, second.Values.Single().Value);
        Assert.NotSame(first.Values, second.Values);
    }

    [Fact]
    public void PasteInto_RecordsSourceAsCopiedFromId()
    {
        var project = new Project();
        var clipboard = new FunctionClipboard();
        var scene = project.CreateScene("Red");
        clipboard.Copy(scene);

        var pasted = clipboard.PasteInto(project);

        Assert.Equal(scene.Id, pasted!.CopiedFromId);
    }

    [Fact]
    public void Copy_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FunctionClipboard().Copy(null!));
    }

    [Fact]
    public void Clear_ThenPaste_ReturnsNull()
    {
        var project = new Project();
        var clipboard = new FunctionClipboard();
        clipboard.Copy(project.CreateScene("Red"));

        clipboard.Clear();

        Assert.Null(clipboard.PasteInto(project));
        Assert.Null(clipboard.FunctionName);
    }

    [Fact]
    public void Copy_TwiceReplacesContent_RaisesChangedEachTime()
    {
        var project = new Project();
        var clipboard = new FunctionClipboard();
        var changes = 0;
        clipboard.Changed += (_, _) => changes++;

        clipboard.Copy(project.CreateScene("A"));
        clipboard.Copy(project.CreateScene("B"));

        Assert.Equal(2, changes);
        Assert.Equal("B", clipboard.FunctionName);
    }

    [Fact]
    public void PasteName_HiddenShowCopyHasCopyName_IsAlsoAvoided()
    {
        var project = new Project();
        var hidden = project.CreateScene("Red (copy)");
        hidden.OwnerShowId = Guid.NewGuid();
        hidden.ShowInFunctionList = false;

        Assert.Equal("Red (copy 2)", FunctionClipboard.PasteName(project, "Red"));
    }

    // ───────────────────────────── RecentProjectList ─────────────────────────────

    [Fact]
    public void Add_NullList_ReturnsOnlyPath()
    {
        Assert.Equal(["a.dmxpro"], RecentProjectList.Add(null, "a.dmxpro"));
    }

    [Fact]
    public void Add_PathWithSurroundingSpaces_StoredTrimmedAndDeduplicated()
    {
        var result = RecentProjectList.Add(["C:\\a.dmxpro"], "  c:\\A.dmxpro ");

        Assert.Equal(["c:\\A.dmxpro"], result);
    }

    [Fact]
    public void Add_ListLongerThanMax_IsCutToMaxWithNewestFirst()
    {
        var existing = Enumerable.Range(1, 20).Select(i => $"p{i}").ToList();

        var result = RecentProjectList.Add(existing, "new");

        Assert.Equal(RecentProjectList.MaxCount, result.Count);
        Assert.Equal("new", result[0]);
        Assert.Equal("p7", result[^1]);
    }

    [Fact]
    public void Add_ExistingEntryAtEndOfFullList_MovesFirstAndKeepsOthers()
    {
        var existing = Enumerable.Range(1, RecentProjectList.MaxCount).Select(i => $"p{i}").ToList();

        var result = RecentProjectList.Add(existing, "P8");

        Assert.Equal(RecentProjectList.MaxCount, result.Count);
        Assert.Equal("P8", result[0]);
        Assert.Equal("p1", result[1]);
        Assert.DoesNotContain("p8", result);
    }

    [Fact]
    public void Add_DoesNotModifyInputList()
    {
        var existing = new List<string> { "a", "b" };

        RecentProjectList.Add(existing, "c");

        Assert.Equal(["a", "b"], existing);
    }

    [Fact]
    public void Add_WhitespacePath_Throws()
    {
        Assert.Throws<ArgumentException>(() => RecentProjectList.Add(["a"], "   "));
        Assert.Throws<ArgumentNullException>(() => RecentProjectList.Add(["a"], null!));
    }

    [Fact]
    public void Remove_NullList_ReturnsEmpty()
    {
        Assert.Empty(RecentProjectList.Remove(null, "a"));
    }

    [Fact]
    public void Normalize_NullEntries_AreDropped()
    {
        var result = RecentProjectList.Normalize(["a", null!, "  ", "A", "b"]);

        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void SamePath_NullsAndSpaces_AreHandled()
    {
        Assert.True(RecentProjectList.SamePath(null, null));
        Assert.False(RecentProjectList.SamePath(null, "a"));
        Assert.True(RecentProjectList.SamePath(" A ", "a"));
    }

    // ───────────────────────────── AddFunctionButton ─────────────────────────────

    [Fact]
    public void AddFunctionButton_PageTooSmallForButton_ReturnsNull()
    {
        var project = new Project();
        var page = project.AddConsolePage("Tiny");
        page.Columns = 1;
        page.Rows = 5;
        var scene = project.CreateScene("Red");

        Assert.Null(project.AddFunctionButton(page.Id, scene.Id));
        Assert.Empty(page.Widgets);
    }

    [Fact]
    public void AddFunctionButton_ZeroSizedPage_ReturnsNull()
    {
        var project = new Project();
        var page = project.AddConsolePage("Zero");
        page.Columns = 0;
        page.Rows = 0;
        var scene = project.CreateScene("Red");

        Assert.Null(project.AddFunctionButton(page.Id, scene.Id));
    }

    [Fact]
    public void AddFunctionButton_NoPagesAtAll_Throws()
    {
        var project = new Project();
        var scene = project.CreateScene("Red");

        Assert.Throws<ArgumentException>(() => project.AddFunctionButton(Guid.NewGuid(), scene.Id));
    }

    [Fact]
    public void AddFunctionButton_FillsPageExactlyThenReturnsNull()
    {
        var project = new Project();
        var page = project.AddConsolePage("Grid");
        page.Columns = ConsoleWidget.DefaultWidth * 2;
        page.Rows = ConsoleWidget.DefaultHeight * 2;
        var scene = project.CreateScene("Red");

        for (var i = 0; i < 4; i++)
        {
            Assert.NotNull(project.AddFunctionButton(page.Id, scene.Id));
        }

        Assert.Null(project.AddFunctionButton(page.Id, scene.Id));
        Assert.Equal(4, page.Widgets.Count);
        Assert.Equal(4, page.Widgets.Select(w => (w.X, w.Y)).Distinct().Count());
    }

    [Fact]
    public void AddFunctionButton_SameFunctionTwice_GivesTwoWidgetsWithDistinctIds()
    {
        var project = new Project();
        var page = project.AddConsolePage("Main");
        var scene = project.CreateScene("Red");

        var first = project.AddFunctionButton(page.Id, scene.Id);
        var second = project.AddFunctionButton(page.Id, scene.Id);

        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Equal(scene.Name, second.Label);
    }
}
