using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>
/// <see cref="Project.FindFixture"/> / <see cref="Project.FindProfile"/> use a cached index; the lists stay public and
/// are edited directly, so every kind of edit must still give the same answer as a linear scan.
/// </summary>
public class ProjectLookupTests
{
    private static Fixture NewFixture(string name) => new() { Name = name, ProfileId = "test-rgbw", ModeName = "4ch" };

    private static FixtureProfile NewProfile(string id) => new() { Id = id, Manufacturer = "Test", Model = id };

    [Fact]
    public void FindFixture_AfterAddRemoveAndInsert_FollowsTheList()
    {
        var project = new Project();
        var a = NewFixture("A");
        var b = NewFixture("B");
        project.Fixtures.AddRange([a, b]);
        Assert.Same(b, project.FindFixture(b.Id)); // builds the index

        project.Fixtures.Remove(a); // shifts B to index 0
        Assert.Same(b, project.FindFixture(b.Id));
        Assert.Null(project.FindFixture(a.Id));

        var c = NewFixture("C");
        project.Fixtures.Insert(0, c);
        Assert.Same(c, project.FindFixture(c.Id));
        Assert.Same(b, project.FindFixture(b.Id));

        project.Fixtures.Add(a);
        Assert.Same(a, project.FindFixture(a.Id));
    }

    [Fact]
    public void FindFixture_ListReplacedOrCleared_UsesNewList()
    {
        var project = new Project();
        var a = NewFixture("A");
        project.Fixtures.Add(a);
        Assert.Same(a, project.FindFixture(a.Id));

        var b = NewFixture("B");
        project.Fixtures = [b];
        Assert.Null(project.FindFixture(a.Id));
        Assert.Same(b, project.FindFixture(b.Id));

        project.Fixtures.Clear();
        Assert.Null(project.FindFixture(b.Id));
    }

    [Fact]
    public void FindFixture_IdChangedInPlace_FindsNewIdOnly()
    {
        var project = new Project();
        var a = NewFixture("A");
        project.Fixtures.Add(a);
        var oldId = a.Id;
        Assert.Same(a, project.FindFixture(oldId));

        a.Id = Guid.NewGuid();

        Assert.Null(project.FindFixture(oldId));
        Assert.Same(a, project.FindFixture(a.Id));
    }

    [Fact]
    public void FindFixture_RemoveFixture_NotFound()
    {
        var project = new Project();
        var added = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 3, 1, 1, "Par");
        Assert.Same(added[2], project.FindFixture(added[2].Id));

        project.RemoveFixture(added[1].Id);

        Assert.Null(project.FindFixture(added[1].Id));
        Assert.Same(added[2], project.FindFixture(added[2].Id));
        Assert.Same(added[0], project.FindFixture(added[0].Id));
    }

    [Fact]
    public void FindProfile_IdIgnoresCase()
    {
        var project = new Project();
        var profile = project.EmbedProfile(NewProfile("Gen-RGB"));

        Assert.Same(profile, project.FindProfile("gen-rgb"));
        Assert.Same(profile, project.FindProfile("GEN-RGB"));
        Assert.Null(project.FindProfile("other"));
    }

    [Fact]
    public void FindProfile_EmbedReplacesCopy_ReturnsNewCopy()
    {
        var project = new Project();
        var first = project.EmbedProfile(NewProfile("p"));
        Assert.Same(first, project.FindProfile("p"));

        var second = project.EmbedProfile(NewProfile("P"));

        Assert.NotSame(first, second);
        Assert.Same(second, project.FindProfile("p"));
    }

    [Fact]
    public void FindProfile_ItemReplacedWithOtherId_OldIdNotFound()
    {
        var project = new Project();
        project.Profiles.AddRange([NewProfile("a"), NewProfile("b")]);
        Assert.NotNull(project.FindProfile("b"));

        project.Profiles[1] = NewProfile("c");

        Assert.Null(project.FindProfile("b"));
        Assert.Equal("c", project.FindProfile("c")!.Id);
        Assert.Equal("a", project.FindProfile("a")!.Id);
    }

    [Fact]
    public void FindProfile_NullIdInList_IsSkipped()
    {
        // Malformed files can deserialize a null id; the lookup must not throw on it.
        var project = new Project();
        project.Profiles.AddRange([new FixtureProfile { Id = null! }, NewProfile("x")]);

        Assert.Equal("x", project.FindProfile("x")!.Id);
        Assert.Null(project.FindProfile("y"));
    }

    [Fact]
    public void FindFixture_ThousandsOfFixtures_EveryIdFound()
    {
        var project = new Project();
        var added = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2000, 1, 1, "Par");

        foreach (var fixture in added)
        {
            Assert.Same(fixture, project.FindFixture(fixture.Id));
        }
    }
}
