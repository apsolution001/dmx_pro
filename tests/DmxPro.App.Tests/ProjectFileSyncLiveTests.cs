using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;
using DmxPro.Engine;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmxPro.App.Tests;

/// <summary>
/// Live reload while Go Live is on (owner decision 2026-10-28): what is on the lights keeps its version, everything else
/// applies at once, the held-back file versions apply when it stops (<see cref="ProjectFileSyncService"/>). Real App
/// services (<see cref="AppRig"/>), temp files, a fake watcher and a manual clock; the engine thread does not run (the
/// console state says what plays; one test ticks the engine by hand for the snapshot path).
/// </summary>
public sealed class ProjectFileSyncLiveTests : IDisposable
{
    private readonly AppRig _rig = new();
    private readonly ManualTimeProvider _time = new();
    private readonly InfoBarState _infoBar;
    private readonly ProjectSerializer _serializer = new();
    private readonly List<Action> _watchers = [];
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"DmxPro.FileSyncLive.Tests-{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly ProjectFileSyncService _sync;

    // Ids in the test project (see OpenProjectAsync).
    private Guid _par1;
    private Guid _par2;
    private Guid _red;
    private Guid _blue;
    private Guid _chaser;
    private Guid _step;
    private Guid _show;
    private Guid _showCopy;
    private Guid _page;
    private Guid _redButton;

    public ProjectFileSyncLiveTests()
    {
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "Live.dmxpro");
        _infoBar = new InfoBarState(_time);
        _sync = new ProjectFileSyncService(
            _rig.Projects,
            _serializer,
            _rig.Dialogs,
            _infoBar,
            _rig.Settings,
            [],
            _rig.LiveMode,
            new PlayingContentService(_rig.Engine, _rig.Playback),
            _time,
            NullLogger<ProjectFileSyncService>.Instance)
        {
            WatcherFactory = (_, onChange) =>
            {
                _watchers.Add(onChange);
                return new Registration(() => _watchers.Remove(onChange));
            },
            RetryDelay = TimeSpan.Zero,
        };
    }

    private Project Project => _rig.Projects.Current;

    public void Dispose()
    {
        _sync.Dispose();
        _infoBar.Dispose();
        _rig.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
    }

    // ───────────── Functions ─────────────

    [Fact]
    public async Task Live_RunningSceneChangedOnDisk_IsHeldBackAndUnrelatedNewSceneApplies()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        var red = Scene(_red);

        await WriteExternallyAsync(p =>
        {
            SceneOf(p, _red).SetValue(_par1, "r", 10);
            p.CreateFunction(FunctionType.Scene, "Green");
        });
        await FileEventAsync();

        Assert.Same(red, Scene(_red));
        Assert.Equal(255, red.Values.Single().Value);
        Assert.Contains(Project.Functions, f => f.Name == "Green");
        Assert.Equal(1, _sync.PendingChangeCount);
        Assert.Contains("waiting", _infoBar.Message, StringComparison.Ordinal);
        Assert.False(_rig.Projects.IsDirty);
        Assert.Equal(DiskHash(), _rig.Projects.DiskContentHash);
    }

    [Fact]
    public async Task Live_RunningSceneStops_PendingChangeApplies()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();
        Assert.Equal(1, _sync.PendingChangeCount);

        _rig.Playback.ToggleFunction(_red);

        Assert.Equal(10, Scene(_red).Values.Single().Value);
        Assert.Equal(0, _sync.PendingChangeCount);
        Assert.False(_rig.Projects.IsDirty);
        Assert.Equal(ProjectFileSyncService.ReloadDescription, _rig.Projects.UndoDescription);
        Assert.Equal(Json(), await File.ReadAllTextAsync(_path));
    }

    [Fact]
    public async Task Live_GoLiveOff_EverythingApplies()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        await WriteExternallyAsync(p => SceneOf(p, _red).Name = "Red (new)");
        await FileEventAsync();
        Assert.Equal("Red", Scene(_red).Name);

        _rig.LiveMode.SetLive(false);

        Assert.Equal("Red (new)", Scene(_red).Name);
        Assert.Equal(0, _sync.PendingChangeCount);
        Assert.True(_rig.Playback.IsFunctionActive(_red));
    }

    [Fact]
    public async Task NotLive_RunningSceneChangedOnDisk_AppliesAtOnce()
    {
        await OpenProjectAsync();
        _rig.Playback.ToggleFunction(_red);

        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();

        Assert.Equal(10, Scene(_red).Values.Single().Value);
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    [Fact]
    public async Task Live_NothingPlays_AppliesAtOnce()
    {
        await OpenProjectAsync();
        GoLive();

        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();

        Assert.Equal(10, Scene(_red).Values.Single().Value);
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    [Fact]
    public async Task Live_RunningChaser_KeepsItsOwnedStepsAndLinkedFunction()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_chaser);
        var chaser = Project.FindFunction(_chaser);
        var step = Project.FindFunction(_step);

        // The file drops the own-look step (and its scene) and changes the linked library scene.
        await WriteExternallyAsync(p =>
        {
            var c = (ChaserFunction)p.FindFunction(_chaser)!;
            c.Steps.RemoveAll(s => s.FunctionId == _step);
            p.Functions.RemoveAll(f => f.Id == _step);
            SceneOf(p, _blue).SetValue(_par2, "b", 1);
        });
        await FileEventAsync();

        Assert.Same(chaser, Project.FindFunction(_chaser));
        Assert.Same(step, Project.FindFunction(_step));
        Assert.Equal(255, Scene(_blue).Values.Single().Value);
        Assert.Equal(3, _sync.PendingChangeCount);

        _rig.Playback.ToggleFunction(_chaser);

        Assert.Null(Project.FindFunction(_step));
        Assert.Equal(1, Scene(_blue).Values.Single().Value);
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    [Fact]
    public async Task Live_FunctionRunningInEngineSnapshot_IsProtected()
    {
        await OpenProjectAsync();
        GoLive();

        // Started without the console (only the engine knows it plays): the snapshot is the source.
        _rig.Engine.StartFunction(_red, EngineLayer.Console);
        _rig.Engine.Tick(TimeSpan.FromMilliseconds(100));
        Assert.Contains(_rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.FunctionId == _red);

        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();
        Assert.Equal(255, Scene(_red).Values.Single().Value);

        _rig.Engine.StopFunction(_red, EngineLayer.Console);
        _rig.Engine.Tick(TimeSpan.FromMilliseconds(100));
        Assert.DoesNotContain(_rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.FunctionId == _red);
        _time.Advance(ProjectFileSyncService.PendingRecheckInterval);

        Assert.Equal(10, Scene(_red).Values.Single().Value);
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    // ───────────── Shows ─────────────

    [Fact]
    public async Task Live_PlayingShow_ShowAndItsCopiesAreProtected()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleShow(_show);
        var show = Project.FindShow(_show);
        var copy = Project.FindFunction(_showCopy);

        await WriteExternallyAsync(p =>
        {
            p.FindShow(_show)!.LengthSeconds = 99;
            p.FindFunction(_showCopy)!.Name = "Renamed copy";
            p.CreateShow("Second show");
        });
        await FileEventAsync();

        Assert.Same(show, Project.FindShow(_show));
        Assert.Same(copy, Project.FindFunction(_showCopy));
        Assert.NotEqual(99, show!.LengthSeconds);
        Assert.Contains(Project.Shows, s => s.Name == "Second show");
        Assert.Equal(2, _sync.PendingChangeCount);

        _rig.Playback.StopShow(_show);

        Assert.Equal(99, Project.FindShow(_show)!.LengthSeconds);
        Assert.Equal("Renamed copy", Project.FindFunction(_showCopy)!.Name);
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    // ───────────── Fixtures ─────────────

    [Fact]
    public async Task Live_FixtureOfRunningFunction_IsProtectedAndUnrelatedFixtureApplies()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);

        await WriteExternallyAsync(p =>
        {
            p.FindFixture(_par1)!.StartAddress = 100;
            p.FindFixture(_par2)!.StartAddress = 200;
        });
        await FileEventAsync();

        Assert.Equal(1, Project.FindFixture(_par1)!.StartAddress);
        Assert.Equal(200, Project.FindFixture(_par2)!.StartAddress);
        Assert.Equal(1, _sync.PendingChangeCount);

        _rig.Playback.ToggleFunction(_red);

        Assert.Equal(100, Project.FindFixture(_par1)!.StartAddress);
    }

    [Fact]
    public async Task Live_ProtectedSceneUsesFixtureDeletedOnDisk_KeepsTheFixtureUntilReleased()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);

        await WriteExternallyAsync(p =>
        {
            p.Fixtures.RemoveAll(f => f.Id == _par1);
            p.Functions.RemoveAll(f => f.Id == _red);
        });
        await FileEventAsync();

        Assert.NotNull(Project.FindFixture(_par1));
        Assert.NotNull(Project.FindFunction(_red));
        Assert.Equal(2, _sync.PendingChangeCount);

        _rig.LiveMode.SetLive(false);

        Assert.Null(Project.FindFixture(_par1));
        Assert.Null(Project.FindFunction(_red));
    }

    // ───────────── Virtual Console ─────────────

    [Fact]
    public async Task Live_ActiveButton_KeptWhileOtherConsoleChangesApply()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);

        await WriteExternallyAsync(p =>
        {
            var page = p.FindConsolePage(_page)!;
            page.Widgets.RemoveAll(w => w.Id == _redButton);
            page.Widgets.Add(new LabelWidget { Label = "New label", X = 10, Y = 10 });
            p.AddConsolePage("Second");
        });
        await FileEventAsync();

        var current = Project.FindConsolePage(_page)!;
        Assert.NotNull(current.FindWidget(_redButton));
        Assert.Contains(current.Widgets, w => w.Label == "New label");
        Assert.Contains(Project.ConsolePages, p => p.Name == "Second");
        Assert.Equal(1, _sync.PendingChangeCount);

        _rig.Playback.ToggleFunction(_red);

        Assert.Null(Project.FindConsolePage(_page)!.FindWidget(_redButton));
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    [Fact]
    public async Task Live_FlashHeld_ConsoleChangesWaitUntilReleased()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.FlashDown(_blue);

        await WriteExternallyAsync(p => p.AddConsolePage("Second"));
        await FileEventAsync();
        Assert.DoesNotContain(Project.ConsolePages, p => p.Name == "Second");
        Assert.Equal(1, _sync.PendingChangeCount);

        _rig.Playback.FlashUp(_blue);

        Assert.Contains(Project.ConsolePages, p => p.Name == "Second");
        Assert.Equal(0, _sync.PendingChangeCount);
    }

    // ───────────── Save / auto-save / newer file ─────────────

    [Fact]
    public async Task Live_PendingChanges_AutoSaveIsPostponed()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();
        var disk = await File.ReadAllTextAsync(_path);
        Project.Name = "Mine";
        _rig.Projects.MarkChanged();

        Assert.Equal(AutoSaveOutcome.Postponed, await _sync.AutoSaveAsync());
        Assert.Equal(disk, await File.ReadAllTextAsync(_path));

        _rig.Playback.ToggleFunction(_red);

        Assert.True(_rig.Projects.IsDirty);
        Assert.Equal(AutoSaveOutcome.Saved, await _sync.AutoSaveAsync());
        var saved = (await _serializer.LoadAsync(_path)).Project;
        Assert.Equal("Mine", saved.Name);
        Assert.Equal(10, SceneOf(saved, _red).Values.Single().Value);
    }

    [Fact]
    public async Task Live_ManualSaveWhilePending_AsksFirst()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();

        _rig.Dialogs.ConfirmAnswer = false;
        Assert.False(await _sync.ConfirmSaveOverPendingAsync(_path));
        Assert.Single(_rig.Dialogs.Prompts);

        // Another file (Save as) holds nothing newer: no question.
        Assert.True(await _sync.ConfirmSaveOverPendingAsync(Path.Combine(_folder, "Other.dmxpro")));
        Assert.Single(_rig.Dialogs.Prompts);

        _rig.Dialogs.ConfirmAnswer = true;
        Assert.True(await _sync.ConfirmSaveOverPendingAsync(_path));
        await _rig.Projects.SaveAsync(_path);

        // The save wrote the playing version: nothing waits any more, and stopping changes nothing.
        Assert.Equal(0, _sync.PendingChangeCount);
        _rig.Playback.ToggleFunction(_red);
        Assert.Equal(255, Scene(_red).Values.Single().Value);
    }

    [Fact]
    public async Task Live_DiskChangesAgainWhilePending_UsesTheNewestContent()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();

        await WriteExternallyAsync(p =>
        {
            SceneOf(p, _red).SetValue(_par1, "r", 20);
            SceneOf(p, _blue).Name = "Blue (new)";
        });
        await FileEventAsync();

        Assert.Equal(255, Scene(_red).Values.Single().Value);
        Assert.Equal("Blue (new)", Scene(_blue).Name);
        Assert.Equal(1, _sync.PendingChangeCount);
        Assert.Empty(_rig.Dialogs.ExternalChangePrompts);

        _rig.Playback.ToggleFunction(_red);

        Assert.Equal(20, Scene(_red).Values.Single().Value);
    }

    [Fact]
    public async Task Live_PendingItemEditedHere_KeepsTheEditAndDropsTheFileVersion()
    {
        await OpenProjectAsync();
        GoLive();
        _rig.Playback.ToggleFunction(_red);
        await WriteExternallyAsync(p => SceneOf(p, _red).SetValue(_par1, "r", 10));
        await FileEventAsync();

        Scene(_red).SetValue(_par1, "r", 77);
        _rig.Projects.MarkChanged();
        _rig.Playback.ToggleFunction(_red);

        Assert.Equal(77, Scene(_red).Values.Single().Value);
        Assert.Equal(0, _sync.PendingChangeCount);
        Assert.True(_rig.Projects.IsDirty);
    }

    // ───────────── Helpers ─────────────

    /// <summary>
    /// Two PARs; scenes Red (PAR 1) and Blue (PAR 2); a chaser with an own-look step (PAR 1) and a linked Blue step; a
    /// show with a show-owned copy; a console page with buttons for Red, Blue and the chaser.
    /// </summary>
    private async Task OpenProjectAsync()
    {
        var project = new Project { Name = "Live" };
        var pars = project.AddFixtures(Rgbw(), "6ch", 2, 1, 1, "PAR");
        _par1 = pars[0].Id;
        _par2 = pars[1].Id;
        var red = (SceneFunction)project.CreateFunction(FunctionType.Scene, "Red");
        red.SetValue(_par1, "r", 255);
        var blue = (SceneFunction)project.CreateFunction(FunctionType.Scene, "Blue");
        blue.SetValue(_par2, "b", 255);
        var chaser = (ChaserFunction)project.CreateFunction(FunctionType.Chaser, "Chase");
        var step = ChaserSteps.NewOwnLookScene(chaser.Id, "Step 1", [new SceneValue { FixtureId = _par1, ChannelKey = "g", Value = 200 }]);
        project.Functions.Add(step);
        chaser.Steps.Add(new ChaserStep { FunctionId = step.Id });
        chaser.Steps.Add(new ChaserStep { FunctionId = blue.Id });
        var show = project.CreateShow("Show");
        var item = project.CreateFunctionInShow(show.Id, show.Tracks[0].Id, FunctionType.Scene, "Show look", 0, 4);
        var page = project.CreateDefaultConsolePage("Main");
        page.Widgets.Clear();
        var redButton = project.AddFunctionButton(page.Id, red.Id)!;
        project.AddFunctionButton(page.Id, blue.Id);
        project.AddFunctionButton(page.Id, chaser.Id);
        _red = red.Id;
        _blue = blue.Id;
        _chaser = chaser.Id;
        _step = step.Id;
        _show = show.Id;
        _showCopy = item.FunctionId;
        _page = page.Id;
        _redButton = redButton.Id;

        await _serializer.SaveAsync(project, _path);
        await _rig.Projects.OpenAsync(_path);

        // The engine thread does not run: load the model so the console can start functions and shows.
        _rig.Engine.LoadProject(_rig.Projects.Current);
        _sync.Start(null);
    }

    private void GoLive()
    {
        _rig.LiveMode.SetLive(true);
        Assert.True(_rig.LiveMode.IsLive);
    }

    private SceneFunction Scene(Guid id) => SceneOf(Project, id);

    private static SceneFunction SceneOf(Project project, Guid id) => (SceneFunction)project.FindFunction(id)!;

    /// <summary>The file as another program would write it: the current file, loaded separately and edited.</summary>
    private async Task WriteExternallyAsync(Action<Project> edit)
    {
        var project = (await _serializer.LoadAsync(_path)).Project;
        edit(project);
        await File.WriteAllTextAsync(_path, _serializer.Serialize(project));
    }

    /// <summary>The watcher reports a change, the quiet period passes, the check finishes.</summary>
    private async Task FileEventAsync()
    {
        foreach (var watcher in _watchers.ToList())
        {
            watcher();
        }

        _time.Advance(ProjectFileSyncService.DebounceDelay);
        await _sync.Idle;
    }

    private string Json() => _serializer.Serialize(Project);

    private string DiskHash() => ProjectSerializer.ComputeContentHash(File.ReadAllBytes(_path));

    private static FixtureProfile Rgbw() => new()
    {
        Id = "test-rgbw-dim",
        Manufacturer = "Test",
        Model = "RGBW",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
            new() { Key = "w", Name = "White", Type = ChannelType.White },
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer, Default = 255 },
            new() { Key = "strobe", Name = "Strobe", Type = ChannelType.Strobe },
        ],
        Modes =
        [
            new() { Name = "6ch", Channels = ["r", "g", "b", "w", "dim", "strobe"] },
        ],
    };

    private sealed class Registration(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
