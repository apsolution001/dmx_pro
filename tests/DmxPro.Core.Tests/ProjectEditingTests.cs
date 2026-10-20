using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class ProjectEditingTests
{
    [Fact]
    public void AddFixtures_Several_PatchesConsecutivelyWithGapAndNumbersNames()
    {
        var project = new Project();

        var added = project.AddFixtures(TestProfiles.Rgbw(), "8ch", count: 3, universe: 2, startAddress: 101, name: "PAR", gap: 2);

        Assert.Equal(["PAR 1", "PAR 2", "PAR 3"], added.Select(f => f.Name));
        Assert.Equal([101, 111, 121], added.Select(f => f.StartAddress));
        Assert.All(added, f => Assert.Equal(2, f.Universe));
        Assert.NotNull(project.FindProfile("test-rgbw"));
    }

    [Fact]
    public void AddFixtures_Single_KeepsNameWithoutNumber()
    {
        var project = new Project();

        var added = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "Stage Left");

        Assert.Equal("Stage Left", Assert.Single(added).Name);
    }

    [Fact]
    public void AddFixtures_UnknownMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Project().AddFixtures(TestProfiles.Rgbw(), "99ch", 1, 1, 1, "X"));
    }

    [Fact]
    public void DuplicateFixture_MovesCopyToNextFreeAddressAndKeepsGroups()
    {
        var project = new Project();
        var source = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
        var group = project.CreateGroup("Front", [source.Id]);

        var copy = project.DuplicateFixture(source.Id);

        Assert.Equal("PAR (copy)", copy.Name);
        Assert.Equal(9, copy.StartAddress);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Contains(copy.Id, group.FixtureIds);
        Assert.DoesNotContain(PatchValidator.Validate(project.Fixtures, project), i => i.Code == "ADDRESS_OVERLAP");
    }

    [Fact]
    public void NextGroupName_NameTaken_SkipsToFreeNumber()
    {
        var project = new Project();
        project.CreateGroup("Group 2", []);

        Assert.Equal("Group 3", project.NextGroupName());
        project.CreateGroup("Front", []);
        Assert.Equal("Group 3", project.NextGroupName());
    }

    [Fact]
    public void AddToGroup_DuplicatesAndUnknownIds_AreSkippedAndOrderKept()
    {
        var project = new Project();
        var f = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 3, 1, 1, "PAR");
        var group = project.CreateGroup("Front", [f[1].Id]);

        var added = project.AddToGroup(group.Id, [f[2].Id, f[1].Id, Guid.NewGuid(), f[0].Id]);

        Assert.Equal(2, added);
        Assert.Equal([f[1].Id, f[2].Id, f[0].Id], group.FixtureIds);
    }

    [Fact]
    public void AddToGroup_UnknownGroup_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Project().AddToGroup(Guid.NewGuid(), []));
    }

    [Fact]
    public void RemoveFromGroup_Member_RemovesOnlyFromGroupAndFixtureStays()
    {
        var project = new Project();
        var f = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "PAR");
        var group = project.CreateGroup("Front", f.Select(x => x.Id));

        Assert.True(project.RemoveFromGroup(group.Id, f[0].Id));
        Assert.False(project.RemoveFromGroup(group.Id, f[0].Id));

        Assert.Equal([f[1].Id], group.FixtureIds);
        Assert.Equal(2, project.Fixtures.Count);
    }

    [Theory]
    [InlineData(0, 1, new[] { 1, 0, 2 })]
    [InlineData(2, -1, new[] { 0, 2, 1 })]
    [InlineData(1, -5, new[] { 1, 0, 2 })]
    [InlineData(1, 5, new[] { 0, 2, 1 })]
    public void MoveInGroup_Delta_ReordersAndClamps(int from, int delta, int[] expectedOrder)
    {
        var project = new Project();
        var f = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 3, 1, 1, "PAR");
        var group = project.CreateGroup("Front", f.Select(x => x.Id));

        Assert.True(project.MoveInGroup(group.Id, f[from].Id, delta));

        Assert.Equal(expectedOrder.Select(i => f[i].Id), group.FixtureIds);
    }

    [Fact]
    public void MoveInGroup_AlreadyAtEndOrNotMember_ReturnsFalse()
    {
        var project = new Project();
        var f = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "PAR");
        var group = project.CreateGroup("Front", [f[0].Id]);

        Assert.False(project.MoveInGroup(group.Id, f[0].Id, -1));
        Assert.False(project.MoveInGroup(group.Id, f[1].Id, 1));
        Assert.False(project.MoveInGroup(Guid.NewGuid(), f[0].Id, 1));
    }

    [Fact]
    public void RenameGroup_TrimsAndRejectsBlank()
    {
        var project = new Project();
        var group = project.CreateGroup("Front", []);

        Assert.True(project.RenameGroup(group.Id, "  Back wash "));
        Assert.Equal("Back wash", group.Name);
        Assert.False(project.RenameGroup(group.Id, "   "));
        Assert.Equal("Back wash", group.Name);
    }

    [Fact]
    public void DeleteGroup_KeepsFixtures()
    {
        var project = new Project();
        var f = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "PAR");
        var group = project.CreateGroup("Front", f.Select(x => x.Id));

        Assert.True(project.DeleteGroup(group.Id));
        Assert.False(project.DeleteGroup(group.Id));

        Assert.Empty(project.Groups);
        Assert.Equal(2, project.Fixtures.Count);
    }

    [Fact]
    public void GroupsOf_Fixture_ReturnsContainingGroupsInOrder()
    {
        var project = new Project();
        var f = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "PAR");
        project.CreateGroup("Front", [f[0].Id]);
        project.CreateGroup("All", f.Select(x => x.Id));

        Assert.Equal(["Front", "All"], project.GroupsOf(f[0].Id).Select(g => g.Name));
        Assert.Equal(["All"], project.GroupsOf(f[1].Id).Select(g => g.Name));
    }

    [Fact]
    public void AddressMap_OverlappingFixtures_ReportsOwnersAndOverlapCount()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "A");
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 7, "B");

        var map = AddressMap.Build(project.Fixtures, project, universe: 1);

        Assert.Equal(10, map.UsedCount);      // 1–8 and 7–10
        Assert.Equal(2, map.OverlapCount);    // 7 and 8
        Assert.Equal(2, map.OwnersAt(8).Count);
        Assert.Empty(map.OwnersAt(11));
    }

    [Fact]
    public void AddressMap_OtherUniverseAndDisabled_AreIgnored()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 2, 1, "Other universe");
        project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "Disabled")[0].Enabled = false;

        Assert.Equal(0, AddressMap.Build(project.Fixtures, project, 1).UsedCount);
    }
}
