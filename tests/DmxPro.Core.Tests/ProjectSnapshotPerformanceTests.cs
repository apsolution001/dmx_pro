using System.Diagnostics;
using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Projects;
using Xunit.Abstractions;

namespace DmxPro.Core.Tests;

/// <summary>
/// Undo snapshots run on the UI thread once the edits pause, so they must stay cheap on big rigs. The bounds are loose
/// (CI machines vary); the measured times are written to the test output.
/// </summary>
public class ProjectSnapshotPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ProjectSnapshotPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>512 fixtures on 8 universes and 100 full-rig scenes (every fixture's R, G, B, W: 204 800 values).</summary>
    private static Project BigProject()
    {
        var project = new Project { Name = "Big" };
        for (var universe = 1; universe <= 8; universe++)
        {
            project.AddFixtures(TestProfiles.Rgbw(), "8ch", 64, universe, 1, $"U{universe} Par");
        }

        for (var s = 0; s < 100; s++)
        {
            var scene = (SceneFunction)project.CreateFunction(FunctionType.Scene, $"Scene {s}");
            foreach (var fixture in project.Fixtures)
            {
                scene.SetValue(fixture.Id, "r", (byte)s);
                scene.SetValue(fixture.Id, "g", 10);
                scene.SetValue(fixture.Id, "b", 20);
                scene.SetValue(fixture.Id, "w", 30);
            }
        }

        project.CreateDefaultConsolePage("Main");
        return project;
    }

    [Fact]
    public void Capture_BigProject_IsFastAndUnchangedStepsCostAlmostNothing()
    {
        var project = BigProject();
        ProjectSnapshot.Capture(project); // warm up the serializer

        var watch = Stopwatch.StartNew();
        var first = ProjectSnapshot.Capture(project);
        var firstMs = watch.Elapsed.TotalMilliseconds;

        ((SceneFunction)project.Functions[3]).SetValue(project.Fixtures[0].Id, "r", 99);
        watch.Restart();
        var second = ProjectSnapshot.Capture(project, first);
        var secondMs = watch.Elapsed.TotalMilliseconds;

        var newBytes = 0L;
        for (var i = 0; i < second.Functions.Count; i++)
        {
            if (!ReferenceEquals(first.Functions.Items[i], second.Functions.Items[i]))
            {
                newBytes += second.Functions.Items[i].Length;
            }
        }

        var history = new ProjectHistory();
        history.Reset(project);
        project.Functions[5].Name = "Renamed";
        history.NoteChange(TimeSpan.Zero);
        watch.Restart();
        history.Undo(project);
        var undoMs = watch.Elapsed.TotalMilliseconds;

        _output.WriteLine($"Snapshot size {first.TotalBytes / 1024} KB; full capture {firstMs:F1} ms; capture after one scene edit {secondMs:F1} ms (new bytes {newBytes / 1024} KB); undo incl. capture {undoMs:F1} ms");
        Assert.True(firstMs < 2000, $"Full capture took {firstMs} ms");
        Assert.True(undoMs < 4000, $"Undo took {undoMs} ms");
        Assert.True(newBytes < first.TotalBytes / 50, "An edit of one scene must not copy the whole project");
    }
}
