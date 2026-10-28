using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>Applying a changed project file only in part while live (<see cref="ExternalChangeMerge"/>, <see cref="ProtectedContent"/>).</summary>
public class ExternalChangeMergeTests
{
    private static readonly ProjectSerializer Serializer = new();

    private static Project Copy(Project project) => Serializer.Deserialize(Serializer.Serialize(project)).Project;

    private static Project Start() => Copy(HistoryTestProjects.Rich());

    private static LightFunction Function(Project project, string name) => project.Functions.Single(f => f.Name == name);

    /// <summary>Plans the reload of <paramref name="external"/> into <paramref name="project"/> and applies it in place.</summary>
    private static (ExternalMergeResult Plan, ProjectSnapshot Disk) Reload(Project project, Project external, ProtectedContent protection)
    {
        var current = ProjectSnapshot.Capture(project);
        var disk = ProjectSnapshot.Capture(external, current);
        var plan = ExternalChangeMerge.PlanReload(current, disk, protection);
        plan.Target.RestoreInto(project, current);
        return (plan, disk);
    }

    private static ExternalMergeResult Release(
        Project project, ProjectSnapshot disk, ExternalMergeResult previous, ProtectedContent protection)
    {
        var current = ProjectSnapshot.Capture(project);
        var plan = ExternalChangeMerge.PlanRelease(current, disk, previous.Target, previous.Deferred, protection);
        plan.Target.RestoreInto(project, current);
        return plan;
    }

    [Fact]
    public void PlanReload_NothingProtected_TakesTheFile()
    {
        var project = Start();
        var external = Copy(project);
        external.Functions[0].Name = "Renamed";

        var (plan, _) = Reload(project, external, ProtectedContent.None);

        Assert.Empty(plan.Deferred);
        Assert.Equal(HistoryTestProjects.Json(external), HistoryTestProjects.Json(project));
    }

    [Fact]
    public void PlanReload_PlayingScene_KeepsItsVersionAndAppliesTheRest()
    {
        var project = Start();
        var red = Function(project, "Red");
        var protection = ProtectedContent.FromPlaying(project, [red.Id], [], flashHeld: false);
        var external = Copy(project);
        Function(external, "Red").Name = "Red (file)";
        external.CreateFunction(FunctionType.Scene, "Green");
        external.Name = "Renamed show";

        var (plan, _) = Reload(project, external, protection);

        Assert.Same(red, project.FindFunction(red.Id));
        Assert.Equal("Red", red.Name);
        Assert.Contains(project.Functions, f => f.Name == "Green");
        Assert.Equal("Renamed show", project.Name);
        Assert.Equal([ProjectItemKey.Function(red.Id)], plan.Deferred);
    }

    [Fact]
    public void FromPlaying_Chaser_ProtectsStepsFixturesAndProfile()
    {
        var project = Start();
        var chaser = Function(project, "Red Blue");

        var protection = ProtectedContent.FromPlaying(project, [chaser.Id], [], flashHeld: false);

        Assert.Contains(ProjectItemKey.Function(Function(project, "Red").Id), protection.FrozenItems);
        Assert.Contains(ProjectItemKey.Function(Function(project, "Blue").Id), protection.FrozenItems);
        Assert.All(project.Fixtures, f => Assert.Contains(ProjectItemKey.Fixture(f.Id), protection.FrozenItems));
        Assert.Contains(ProjectItemKey.Profile(project.Profiles[0].Id), protection.FrozenItems);
        Assert.DoesNotContain(ProjectItemKey.Group(project.Groups[0].Id), protection.FrozenItems);
    }

    [Fact]
    public void PlanReload_FlashHeldAndPageDeletedOnDisk_KeepsPageAndWhatItNeeds()
    {
        var project = Start();
        var page = project.ConsolePages[0];
        var blue = Function(project, "Blue");
        var protection = ProtectedContent.FromPlaying(project, [], [], flashHeld: true);
        var external = Copy(project);
        external.ConsolePages.Clear();
        external.Functions.RemoveAll(f => f.Id == blue.Id);

        var (plan, disk) = Reload(project, external, protection);

        Assert.Same(page, project.ConsolePages.Single());
        Assert.Same(blue, project.FindFunction(blue.Id));
        Assert.Equal(2, plan.Deferred.Count);
        Assert.False(ExternalChangeMerge.HasReleasable(plan.Deferred, disk, protection));
        Assert.True(ExternalChangeMerge.HasReleasable(plan.Deferred, disk, ProtectedContent.None));

        var released = Release(project, disk, plan, ProtectedContent.None);

        Assert.Empty(released.Deferred);
        Assert.Empty(project.ConsolePages);
        Assert.Null(project.FindFunction(blue.Id));
    }

    [Fact]
    public void PlanRelease_OnlyItemsNoLongerProtected_AreApplied()
    {
        var project = Start();
        var red = Function(project, "Red");
        var blue = Function(project, "Blue");
        var external = Copy(project);
        Function(external, "Red").Name = "Red (file)";
        Function(external, "Blue").Name = "Blue (file)";
        var (plan, disk) = Reload(project, external, ProtectedContent.FromPlaying(project, [red.Id, blue.Id], [], flashHeld: false));
        Assert.Equal(2, plan.Deferred.Count);

        var released = Release(project, disk, plan, ProtectedContent.FromPlaying(project, [blue.Id], [], flashHeld: false));

        Assert.Equal("Red (file)", project.FindFunction(red.Id)!.Name);
        Assert.Equal("Blue", project.FindFunction(blue.Id)!.Name);
        Assert.Equal([ProjectItemKey.Function(blue.Id)], released.Deferred);
    }

    [Fact]
    public void PlanRelease_ItemEditedSinceTheReload_KeepsTheEdit()
    {
        var project = Start();
        var red = Function(project, "Red");
        var external = Copy(project);
        Function(external, "Red").Name = "Red (file)";
        var (plan, disk) = Reload(project, external, ProtectedContent.FromPlaying(project, [red.Id], [], flashHeld: false));
        red.Name = "Red (mine)";

        var released = Release(project, disk, plan, ProtectedContent.None);

        Assert.Equal("Red (mine)", red.Name);
        Assert.Empty(released.Deferred);
        Assert.Equal(1, released.Discarded);
    }

    [Fact]
    public void PlanReload_ActiveButtonRemovedOnDisk_KeptInsideTheFilesPage()
    {
        var project = Start();
        var red = Function(project, "Red");
        var page = project.ConsolePages[0];
        var button = page.Widgets.OfType<DmxPro.Core.VirtualConsole.FunctionWidget>().First(w => w.FunctionId == red.Id);
        var external = Copy(project);
        var externalPage = external.ConsolePages[0];
        externalPage.Widgets.RemoveAll(w => w.Id == button.Id);
        externalPage.Name = "Renamed page";

        var (plan, disk) = Reload(project, external, ProtectedContent.FromPlaying(project, [red.Id], [], flashHeld: false));

        Assert.Equal("Renamed page", project.ConsolePages[0].Name);
        Assert.NotNull(project.ConsolePages[0].FindWidget(button.Id));
        Assert.Equal([ProjectItemKey.ConsoleWidget(page.Id, button.Id)], plan.Deferred);

        Release(project, disk, plan, ProtectedContent.None);

        Assert.Null(project.ConsolePages[0].FindWidget(button.Id));
        Assert.Equal(HistoryTestProjects.Json(external), HistoryTestProjects.Json(project));
    }
}
