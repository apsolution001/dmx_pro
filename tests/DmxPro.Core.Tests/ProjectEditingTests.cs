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
