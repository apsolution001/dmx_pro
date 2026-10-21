using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class RecentProjectListTests
{
    [Fact]
    public void Add_EmptyList_ReturnsOnlyPath()
    {
        Assert.Equal([@"C:\Shows\a.dmxpro"], RecentProjectList.Add(null, @"C:\Shows\a.dmxpro"));
    }

    [Fact]
    public void Add_NewPath_PutsItFirst()
    {
        var list = RecentProjectList.Add([@"C:\a.dmxpro", @"C:\b.dmxpro"], @"C:\c.dmxpro");

        Assert.Equal([@"C:\c.dmxpro", @"C:\a.dmxpro", @"C:\b.dmxpro"], list);
    }

    [Fact]
    public void Add_ExistingPathDifferentCase_MovesItFirstWithoutDuplicate()
    {
        var list = RecentProjectList.Add([@"C:\a.dmxpro", @"C:\B.dmxpro"], @"c:\b.DMXPRO");

        Assert.Equal([@"c:\b.DMXPRO", @"C:\a.dmxpro"], list);
    }

    [Fact]
    public void Add_FullList_DropsOldest()
    {
        var full = Enumerable.Range(1, RecentProjectList.MaxCount).Select(i => $@"C:\{i}.dmxpro").ToList();

        var list = RecentProjectList.Add(full, @"C:\new.dmxpro");

        Assert.Equal(RecentProjectList.MaxCount, list.Count);
        Assert.Equal(@"C:\new.dmxpro", list[0]);
        Assert.DoesNotContain($@"C:\{RecentProjectList.MaxCount}.dmxpro", list);
    }

    [Fact]
    public void Add_BlankPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => RecentProjectList.Add([], "  "));
    }

    [Fact]
    public void Remove_PathDifferentCase_RemovesIt()
    {
        var list = RecentProjectList.Remove([@"C:\a.dmxpro", @"C:\b.dmxpro"], @"C:\A.DMXPRO");

        Assert.Equal([@"C:\b.dmxpro"], list);
    }

    [Fact]
    public void Remove_UnknownPath_KeepsList()
    {
        Assert.Equal([@"C:\a.dmxpro"], RecentProjectList.Remove([@"C:\a.dmxpro"], @"C:\x.dmxpro"));
    }

    [Fact]
    public void Normalize_BlanksDuplicatesAndTooMany_Cleaned()
    {
        var input = new List<string> { " ", @"C:\a.dmxpro", @"c:\A.dmxpro", null!, " C:\\b.dmxpro " };
        input.AddRange(Enumerable.Range(1, 20).Select(i => $@"C:\{i}.dmxpro"));

        var list = RecentProjectList.Normalize(input);

        Assert.Equal(RecentProjectList.MaxCount, list.Count);
        Assert.Equal(@"C:\a.dmxpro", list[0]);
        Assert.Equal(@"C:\b.dmxpro", list[1]);
    }
}
