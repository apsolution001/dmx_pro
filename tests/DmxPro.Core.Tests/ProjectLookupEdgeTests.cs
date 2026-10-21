using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>More list-mutation paths for the cached <see cref="Project.FindFixture"/> / <see cref="Project.FindProfile"/>.</summary>
public class ProjectLookupEdgeTests
{
    private static Fixture NewFixture(string name) => new() { Name = name, ProfileId = "test-rgbw", ModeName = "4ch" };

    private static FixtureProfile NewProfile(string id) => new() { Id = id, Manufacturer = "Test", Model = id };

    [Fact]
    public void FindFixture_AfterAddFixturesAndRemoveFixture_MatchesLinearScan()
    {
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 5, 1, 1, "PAR");
        Assert.All(fixtures, f => Assert.Same(f, project.FindFixture(f.Id)));

        Assert.True(project.RemoveFixture(fixtures[1].Id));

        Assert.Null(project.FindFixture(fixtures[1].Id));
        foreach (var f in project.Fixtures)
        {
            Assert.Same(f, project.FindFixture(f.Id));
        }
    }

    [Fact]
    public void FindFixture_DuplicateIds_ReturnsFirstThenSecondAfterFirstRemoved()
    {
        var project = new Project();
        var first = NewFixture("first");
        var second = NewFixture("second");
        second.Id = first.Id;
        project.Fixtures.AddRange([first, second]);

        Assert.Same(first, project.FindFixture(first.Id));
        project.Fixtures.Remove(first);

        Assert.Same(second, project.FindFixture(first.Id));
    }

    [Fact]
    public void FindFixture_ItemsSwappedAndListReversed_StillFound()
    {
        var project = new Project();
        var a = NewFixture("A");
        var b = NewFixture("B");
        var c = NewFixture("C");
        project.Fixtures.AddRange([a, b, c]);
        Assert.Same(c, project.FindFixture(c.Id));

        (project.Fixtures[0], project.Fixtures[2]) = (project.Fixtures[2], project.Fixtures[0]);
        Assert.Same(a, project.FindFixture(a.Id));
        Assert.Same(b, project.FindFixture(b.Id));
        Assert.Same(c, project.FindFixture(c.Id));

        project.Fixtures.Reverse();
        Assert.Same(a, project.FindFixture(a.Id));
        Assert.Same(c, project.FindFixture(c.Id));
    }

    [Fact]
    public void FindFixture_LastItemRemoved_IndexBeyondListIsNotUsed()
    {
        var project = new Project();
        var a = NewFixture("A");
        var b = NewFixture("B");
        project.Fixtures.AddRange([a, b]);
        Assert.Same(b, project.FindFixture(b.Id));

        project.Fixtures.RemoveAt(1);

        Assert.Null(project.FindFixture(b.Id));
        Assert.Same(a, project.FindFixture(a.Id));
    }

    [Fact]
    public void FindFixture_NullItemInList_DoesNotThrow()
    {
        var project = new Project();
        var a = NewFixture("A");
        project.Fixtures.AddRange([null!, a]);

        Assert.Same(a, project.FindFixture(a.Id));
        Assert.Null(project.FindFixture(Guid.NewGuid()));
    }

    [Fact]
    public void FindFixture_EmptyGuid_ReturnsNullWhenNoFixtureHasIt()
    {
        var project = new Project();
        project.Fixtures.Add(NewFixture("A"));

        Assert.Null(project.FindFixture(Guid.Empty));
    }

    [Fact]
    public void FindFixture_AfterSaveAndLoad_FindsEveryFixture()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 4, 1, 1, "PAR");
        var serializer = new ProjectSerializer();

        var loaded = serializer.Deserialize(serializer.Serialize(project)).Project;

        Assert.Equal(4, loaded.Fixtures.Count);
        Assert.All(loaded.Fixtures, f => Assert.Same(f, loaded.FindFixture(f.Id)));
        Assert.All(loaded.Fixtures, f => Assert.NotNull(loaded.FindProfile(f.ProfileId)));
    }

    [Fact]
    public void FindProfile_EmbedWithDifferentCase_ReplacesAndFindsCopy()
    {
        var project = new Project();
        var original = project.EmbedProfile(TestProfiles.Rgbw());
        Assert.Same(original, project.FindProfile("test-rgbw"));

        var other = TestProfiles.Rgbw();
        other.Id = "TEST-RGBW";
        var replaced = project.EmbedProfile(other);

        Assert.Single(project.Profiles);
        Assert.Same(replaced, project.FindProfile("test-rgbw"));
        Assert.Same(replaced, project.FindProfile("Test-Rgbw"));
    }

    [Fact]
    public void FindProfile_DuplicateIdsDifferentCase_ReturnsFirstInList()
    {
        var project = new Project();
        var lower = NewProfile("abc");
        var upper = NewProfile("ABC");
        project.Profiles.AddRange([lower, upper]);

        Assert.Same(lower, project.FindProfile("Abc"));
        project.Profiles.Remove(lower);
        Assert.Same(upper, project.FindProfile("abc"));
    }

    [Fact]
    public void FindProfile_UnknownEmptyOrNullId_ReturnsNull()
    {
        var project = new Project();
        project.Profiles.Add(NewProfile("abc"));
        Assert.NotNull(project.FindProfile("abc"));

        Assert.Null(project.FindProfile("zzz"));
        Assert.Null(project.FindProfile(string.Empty));
        Assert.Null(project.FindProfile(null!));
    }

    [Fact]
    public void FindProfile_ProfilesListReplacedByAssignment_UsesNewList()
    {
        var project = new Project();
        project.Profiles.Add(NewProfile("old"));
        Assert.NotNull(project.FindProfile("old"));

        var fresh = NewProfile("fresh");
        project.Profiles = [fresh];

        Assert.Null(project.FindProfile("old"));
        Assert.Same(fresh, project.FindProfile("FRESH"));
    }

    [Fact]
    public void FindProfile_UpdateProfile_ReturnsUpdatedCopy()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "PAR");
        var before = project.FindProfile("test-rgbw");

        var edited = TestProfiles.Rgbw();
        edited.Model = "Edited";
        project.UpdateProfile(edited);

        var after = project.FindProfile("test-rgbw");
        Assert.NotNull(after);
        Assert.NotSame(before, after);
        Assert.Equal("Edited", after.Model);
    }
}
