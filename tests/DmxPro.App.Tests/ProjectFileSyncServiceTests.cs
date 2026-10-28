using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui.Controls;

namespace DmxPro.App.Tests;

/// <summary>
/// Live reload of the open project file and auto-save (<see cref="ProjectFileSyncService"/>), on the real App services
/// (<see cref="AppRig"/>), real temp files, a fake watcher and a manual clock.
/// </summary>
public sealed class ProjectFileSyncServiceTests : IDisposable
{
    private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromMinutes(AppSettings.DefaultAutosaveIntervalMinutes);

    private readonly AppRig _rig = new();
    private readonly ManualTimeProvider _time = new();
    private readonly InfoBarState _infoBar;
    private readonly ProjectSerializer _serializer = new();
    private readonly FakeDraftSource _drafts = new();
    private readonly FakeWatcher _watcher = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"DmxPro.FileSync.Tests-{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly ProjectFileSyncService _sync;

    public ProjectFileSyncServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "Show.dmxpro");
        _infoBar = new InfoBarState(_time);
        _sync = new ProjectFileSyncService(
            _rig.Projects,
            _serializer,
            _rig.Dialogs,
            _infoBar,
            _rig.Settings,
            [_drafts],
            _time,
            NullLogger<ProjectFileSyncService>.Instance)
        {
            WatcherFactory = _watcher.Create,
            RetryDelay = TimeSpan.Zero,
        };
    }

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

    // ───────────── Live reload ─────────────

    [Fact]
    public async Task ExternalChange_AddsScene_AppearsInPlaceWithoutReopen()
    {
        await OpenProjectAsync();
        var project = _rig.Projects.Current;
        var red = project.Functions[0];
        var opened = 0;
        var restored = 0;
        _rig.Projects.ProjectChanged += (_, _) => opened++;
        _rig.Projects.ContentRestored += (_, _) => restored++;

        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await FileEventAsync();

        Assert.Same(project, _rig.Projects.Current);
        Assert.Contains(project.Functions, f => f.Name == "Blue");
        Assert.Same(red, project.Functions[0]);
        Assert.Equal(0, opened);
        Assert.Equal(1, restored);
        Assert.False(_rig.Projects.IsDirty);
        Assert.Equal(DiskHash(), _rig.Projects.DiskContentHash);
        Assert.Empty(_rig.Dialogs.ExternalChangePrompts);
        Assert.Equal(InfoBarSeverity.Success, _infoBar.Severity);
    }

    [Fact]
    public async Task ExternalChange_WhileLive_LiveModeAndOutputStateStay()
    {
        await OpenProjectAsync();
        _rig.StartEngine();
        _rig.LiveMode.SetLive(true);
        var outputEnabled = _rig.Output.IsEnabled;

        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await FileEventAsync();

        Assert.Contains(_rig.Projects.Current.Functions, f => f.Name == "Blue");
        Assert.True(_rig.LiveMode.IsLive);
        Assert.True(_rig.Engine.IsLiveMode);
        Assert.Equal(outputEnabled, _rig.Output.IsEnabled);
    }

    [Fact]
    public async Task OwnSave_FileEvent_DoesNotReload()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Renamed";
        _rig.Projects.MarkChanged();
        await _rig.Projects.SaveAsync(_path);
        var restored = 0;
        _rig.Projects.ContentRestored += (_, _) => restored++;
        var undo = _rig.Projects.UndoDescription;

        await FileEventAsync();

        Assert.Equal(0, restored);
        Assert.Equal(undo, _rig.Projects.UndoDescription);
        Assert.Empty(_rig.Dialogs.ExternalChangePrompts);
        Assert.Null(_infoBar.Message);
    }

    [Fact]
    public async Task Undo_AfterReload_RestoresPreviousContent()
    {
        await OpenProjectAsync();
        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await FileEventAsync();
        Assert.Equal(ProjectFileSyncService.ReloadDescription, _rig.Projects.UndoDescription);

        Assert.True(_rig.Projects.TryUndo(out var description));

        Assert.Equal(ProjectFileSyncService.ReloadDescription, description);
        Assert.DoesNotContain(_rig.Projects.Current.Functions, f => f.Name == "Blue");
        Assert.True(_rig.Projects.IsDirty);
    }

    [Fact]
    public async Task ExternalChange_UnsavedChangesAndReload_AppliesAndUndoBringsChangesBack()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Mine";
        _rig.Projects.MarkChanged();
        _rig.Dialogs.ExternalChangeAnswer = ExternalChangeChoice.Reload;

        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await FileEventAsync();

        Assert.Equal("Show.dmxpro", Assert.Single(_rig.Dialogs.ExternalChangePrompts));
        Assert.Contains(_rig.Projects.Current.Functions, f => f.Name == "Blue");
        Assert.Equal("Show", _rig.Projects.Current.Name);
        Assert.False(_rig.Projects.IsDirty);

        Assert.True(_rig.Projects.TryUndo(out _));
        Assert.Equal("Mine", _rig.Projects.Current.Name);
        Assert.DoesNotContain(_rig.Projects.Current.Functions, f => f.Name == "Blue");
        Assert.True(_rig.Projects.IsDirty);
    }

    [Fact]
    public async Task ExternalChange_UnsavedChangesAndKeepMine_KeepsProjectAndDoesNotAskAgain()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Mine";
        _rig.Projects.MarkChanged();
        _rig.Dialogs.ExternalChangeAnswer = ExternalChangeChoice.KeepMine;

        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await FileEventAsync();
        await FileEventAsync();

        Assert.Single(_rig.Dialogs.ExternalChangePrompts);
        Assert.Equal("Mine", _rig.Projects.Current.Name);
        Assert.DoesNotContain(_rig.Projects.Current.Functions, f => f.Name == "Blue");
        Assert.True(_rig.Projects.IsDirty);
        Assert.Equal(DiskHash(), _rig.Projects.DiskContentHash);

        // The next save overwrites the file with this version.
        Assert.Equal(AutoSaveOutcome.Saved, await _sync.AutoSaveAsync());
        Assert.Equal("Mine", (await _serializer.LoadAsync(_path)).Project.Name);
    }

    [Fact]
    public async Task ExternalChange_EditorDraftOpen_AsksFirst()
    {
        await OpenProjectAsync();
        _drafts.HasUnsavedDraft = true;
        _rig.Dialogs.ExternalChangeAnswer = ExternalChangeChoice.KeepMine;

        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await FileEventAsync();

        Assert.Single(_rig.Dialogs.ExternalChangePrompts);
        Assert.DoesNotContain(_rig.Projects.Current.Functions, f => f.Name == "Blue");
    }

    [Fact]
    public async Task ExternalChange_CorruptFile_ShowsWarningAndKeepsProject()
    {
        await OpenProjectAsync();
        var before = _serializer.Serialize(_rig.Projects.Current);
        var restored = 0;
        _rig.Projects.ContentRestored += (_, _) => restored++;

        await File.WriteAllTextAsync(_path, "{ \"schemaVersion\": 1, \"functions\": [");
        await FileEventAsync();

        Assert.Equal(InfoBarSeverity.Warning, _infoBar.Severity);
        Assert.StartsWith("Project file changed on disk but could not be read:", _infoBar.Message);
        Assert.Equal(before, _serializer.Serialize(_rig.Projects.Current));
        Assert.Equal(0, restored);
        Assert.False(_rig.Projects.IsDirty);
    }

    [Fact]
    public async Task ExternalChange_HalfWrittenThenCompleted_AppliesCompletedVersion()
    {
        await OpenProjectAsync();
        var complete = await ExternalJsonAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await File.WriteAllTextAsync(_path, complete[..(complete.Length / 2)]);
        await FileEventAsync();
        Assert.Equal(InfoBarSeverity.Warning, _infoBar.Severity);

        await File.WriteAllTextAsync(_path, complete);
        await FileEventAsync();

        Assert.Contains(_rig.Projects.Current.Functions, f => f.Name == "Blue");
        Assert.Equal(InfoBarSeverity.Success, _infoBar.Severity);
    }

    [Fact]
    public async Task ExternalChange_DeletedFunction_IsRemoved()
    {
        await OpenProjectAsync();
        var id = _rig.Projects.Current.Functions[0].Id;

        await WriteExternallyAsync(p => p.Functions.RemoveAll(f => f.Id == id));
        await FileEventAsync();

        Assert.Null(_rig.Projects.Current.FindFunction(id));
    }

    [Fact]
    public async Task FileEvents_InQuickSuccession_ReadOnceAfterQuietPeriod()
    {
        await OpenProjectAsync();
        var restored = 0;
        _rig.Projects.ContentRestored += (_, _) => restored++;
        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));

        _watcher.Fire();
        _time.Advance(ProjectFileSyncService.DebounceDelay / 2);
        _watcher.Fire();
        _time.Advance(ProjectFileSyncService.DebounceDelay / 2);
        await _sync.Idle;
        Assert.Equal(0, restored);

        _time.Advance(ProjectFileSyncService.DebounceDelay / 2);
        await _sync.Idle;
        Assert.Equal(1, restored);
    }

    [Fact]
    public async Task ExternalChange_DialogOpen_WaitsUntilItCloses()
    {
        await OpenProjectAsync();
        _rig.Dialogs.IsModalOpen = true;
        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));

        await FileEventAsync();
        Assert.DoesNotContain(_rig.Projects.Current.Functions, f => f.Name == "Blue");

        _rig.Dialogs.IsModalOpen = false;
        _time.Advance(ProjectFileSyncService.ModalRecheckDelay);
        await _sync.Idle;
        Assert.Contains(_rig.Projects.Current.Functions, f => f.Name == "Blue");
    }

    [Fact]
    public async Task ReloadSettingOff_ExternalChange_IsIgnoredAndNotWatched()
    {
        await OpenProjectAsync();
        Assert.Equal(_path, _sync.WatchedPath);

        _rig.Settings.Update(s => s.ReloadProjectOnDiskChange = false);
        await WriteExternallyAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await _sync.CheckForExternalChangeAsync();

        Assert.Null(_sync.WatchedPath);
        Assert.Equal(0, _watcher.Active);
        Assert.DoesNotContain(_rig.Projects.Current.Functions, f => f.Name == "Blue");
    }

    [Fact]
    public async Task SaveAs_NewPath_WatcherFollows()
    {
        await OpenProjectAsync();
        var other = Path.Combine(_folder, "Other.dmxpro");

        await _rig.Projects.SaveAsync(other);

        Assert.Equal(other, _sync.WatchedPath);
        Assert.Equal(other, _watcher.LastPath);
        Assert.Equal(1, _watcher.Active);
    }

    [Fact]
    public void UntitledProject_IsNotWatched()
    {
        _sync.Start(null);
        _rig.Projects.New();

        Assert.Null(_sync.WatchedPath);
        Assert.Equal(0, _watcher.Active);
    }

    // ───────────── Auto-save ─────────────

    [Fact]
    public async Task AutoSave_AfterIntervalWhenDirty_SavesThroughNormalPath()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();

        _time.Advance(AutoSaveInterval - TimeSpan.FromSeconds(1));
        await _sync.Idle;
        Assert.Equal("Show", (await _serializer.LoadAsync(_path)).Project.Name);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _sync.Idle;

        Assert.Equal("Edited", (await _serializer.LoadAsync(_path)).Project.Name);
        Assert.False(_rig.Projects.IsDirty);
        Assert.Equal(DiskHash(), _rig.Projects.DiskContentHash);
        Assert.Equal(InfoBarSeverity.Success, _infoBar.Severity);
        Assert.StartsWith("Auto-saved ", _infoBar.Message);
    }

    [Fact]
    public async Task AutoSave_OwnSave_DoesNotTriggerReload()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();
        Assert.Equal(AutoSaveOutcome.Saved, await _sync.AutoSaveAsync());
        var restored = 0;
        _rig.Projects.ContentRestored += (_, _) => restored++;

        await FileEventAsync();

        Assert.Equal(0, restored);
        Assert.Empty(_rig.Dialogs.ExternalChangePrompts);
    }

    [Fact]
    public async Task AutoSave_NotDirty_DoesNotWrite()
    {
        await OpenProjectAsync();
        var before = await File.ReadAllBytesAsync(_path);
        var writeTime = File.GetLastWriteTimeUtc(_path);

        _time.Advance(AutoSaveInterval);
        await _sync.Idle;

        Assert.Equal(AutoSaveOutcome.NotNeeded, await _sync.AutoSaveAsync());
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public async Task AutoSave_UntitledProject_NeverSaves()
    {
        _sync.Start(null);
        _rig.Projects.New();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();

        _time.Advance(AutoSaveInterval * 3);
        await _sync.Idle;

        Assert.Equal(AutoSaveOutcome.NotNeeded, await _sync.AutoSaveAsync());
        Assert.Null(_rig.Projects.FilePath);
        Assert.True(_rig.Projects.IsDirty);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AutoSave_UnappliedExternalChange_DoesNotOverwriteAndAsks()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Mine";
        _rig.Projects.MarkChanged();
        _rig.Dialogs.ExternalChangeAnswer = ExternalChangeChoice.KeepMine;
        var external = await ExternalJsonAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await File.WriteAllTextAsync(_path, external);

        Assert.Equal(AutoSaveOutcome.DiskChanged, await _sync.AutoSaveAsync());
        await _sync.Idle;

        Assert.Equal(external, await File.ReadAllTextAsync(_path));
        Assert.Single(_rig.Dialogs.ExternalChangePrompts);

        // Keep mine was chosen: now the next auto-save may overwrite it.
        Assert.Equal(AutoSaveOutcome.Saved, await _sync.AutoSaveAsync());
        Assert.Equal("Mine", (await _serializer.LoadAsync(_path)).Project.Name);
    }

    [Fact]
    public async Task AutoSave_UnappliedExternalChangeWithReloadOff_WarnsAndDoesNotOverwrite()
    {
        await OpenProjectAsync();
        _rig.Settings.Update(s => s.ReloadProjectOnDiskChange = false);
        _rig.Projects.Current.Name = "Mine";
        _rig.Projects.MarkChanged();
        var external = await ExternalJsonAsync(p => p.CreateFunction(FunctionType.Scene, "Blue"));
        await File.WriteAllTextAsync(_path, external);

        Assert.Equal(AutoSaveOutcome.DiskChanged, await _sync.AutoSaveAsync());

        Assert.Equal(external, await File.ReadAllTextAsync(_path));
        Assert.Equal(InfoBarSeverity.Warning, _infoBar.Severity);
        Assert.Empty(_rig.Dialogs.ExternalChangePrompts);
    }

    [Fact]
    public async Task AutoSave_DialogOpen_IsPostponedAndRetried()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();
        _rig.Dialogs.IsModalOpen = true;

        _time.Advance(AutoSaveInterval);
        await _sync.Idle;
        Assert.Equal("Show", (await _serializer.LoadAsync(_path)).Project.Name);

        _rig.Dialogs.IsModalOpen = false;
        _time.Advance(ProjectFileSyncService.PostponeDelay);
        await _sync.Idle;
        Assert.Equal("Edited", (await _serializer.LoadAsync(_path)).Project.Name);
    }

    [Fact]
    public async Task AutoSave_Suspended_IsPostponed()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();

        using (_sync.Suspend())
        {
            Assert.Equal(AutoSaveOutcome.Postponed, await _sync.AutoSaveAsync());
        }

        Assert.Equal(AutoSaveOutcome.Saved, await _sync.AutoSaveAsync());
    }

    [Fact]
    public async Task AutoSave_SettingOff_NeverSaves()
    {
        await OpenProjectAsync();
        _rig.Settings.Update(s => s.AutoSaveEnabled = false);
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();

        _time.Advance(AutoSaveInterval * 2);
        await _sync.Idle;

        Assert.Equal(AutoSaveOutcome.NotNeeded, await _sync.AutoSaveAsync());
        Assert.Equal("Show", (await _serializer.LoadAsync(_path)).Project.Name);
        Assert.True(_rig.Projects.IsDirty);
    }

    [Fact]
    public async Task AutoSave_CustomInterval_SavesAtThatInterval()
    {
        _rig.Settings.Update(s => s.AutosaveIntervalMinutes = 1);
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();

        _time.Advance(TimeSpan.FromMinutes(1));
        await _sync.Idle;

        Assert.Equal("Edited", (await _serializer.LoadAsync(_path)).Project.Name);
    }

    [Fact]
    public async Task Stop_AfterClose_NoMoreAutoSaves()
    {
        await OpenProjectAsync();
        _rig.Projects.Current.Name = "Edited";
        _rig.Projects.MarkChanged();

        _sync.Stop();
        _time.Advance(AutoSaveInterval * 2);

        Assert.Equal(AutoSaveOutcome.NotNeeded, await _sync.AutoSaveAsync());
        Assert.Equal("Show", (await _serializer.LoadAsync(_path)).Project.Name);
        Assert.Equal(0, _watcher.Active);
    }

    // ───────────── Real watcher ─────────────

    [Fact]
    public async Task RealWatcher_FileReplacedByRename_IsApplied()
    {
        await _rig.Projects.SaveAsync(_path);
        _rig.Projects.Current.CreateFunction(FunctionType.Scene, "Red");
        _rig.Projects.MarkChanged();
        await _rig.Projects.SaveAsync(_path);
        using var sync = new ProjectFileSyncService(
            _rig.Projects, _serializer, _rig.Dialogs, _infoBar, _rig.Settings, [_drafts], TimeProvider.System,
            NullLogger<ProjectFileSyncService>.Instance)
        {
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        sync.Start(null);

        // Like many editors: write a temp file, then rename it over the project.
        var temp = _path + ".tmp~";
        await File.WriteAllTextAsync(temp, await ExternalJsonAsync(p => p.CreateFunction(FunctionType.Scene, "Blue")));
        File.Move(temp, _path, overwrite: true);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!_rig.Projects.Current.Functions.Any(f => f.Name == "Blue") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await sync.Idle;
        Assert.Contains(_rig.Projects.Current.Functions, f => f.Name == "Blue");
    }

    // ───────────── Helpers ─────────────

    /// <summary>A saved project with one scene "Red", opened from <see cref="_path"/>, and the service started.</summary>
    private async Task OpenProjectAsync()
    {
        var project = new Project { Name = "Show" };
        project.CreateFunction(FunctionType.Scene, "Red");
        await _serializer.SaveAsync(project, _path);
        await _rig.Projects.OpenAsync(_path);
        _sync.Start(null);
    }

    /// <summary>The file as another program would write it: the current file, loaded separately and edited.</summary>
    private async Task<string> ExternalJsonAsync(Action<Project> edit)
    {
        var project = (await _serializer.LoadAsync(_path)).Project;
        edit(project);
        return _serializer.Serialize(project);
    }

    private async Task WriteExternallyAsync(Action<Project> edit) =>
        await File.WriteAllTextAsync(_path, await ExternalJsonAsync(edit));

    /// <summary>The watcher reports a change, the quiet period passes, the check finishes.</summary>
    private async Task FileEventAsync()
    {
        _watcher.Fire();
        _time.Advance(ProjectFileSyncService.DebounceDelay);
        await _sync.Idle;
    }

    private string DiskHash() => ProjectSerializer.ComputeContentHash(File.ReadAllBytes(_path));

    private sealed class FakeDraftSource : IUnsavedDraftSource
    {
        public bool HasUnsavedDraft { get; set; }
    }

    /// <summary>Stands in for the FileSystemWatcher: the test fires the change itself.</summary>
    private sealed class FakeWatcher
    {
        private readonly List<Action> _callbacks = [];

        public int Active => _callbacks.Count;

        public string? LastPath { get; private set; }

        public IDisposable Create(string path, Action onChange)
        {
            LastPath = path;
            _callbacks.Add(onChange);
            return new Registration(() => _callbacks.Remove(onChange));
        }

        public void Fire()
        {
            foreach (var callback in _callbacks.ToList())
            {
                callback();
            }
        }

        private sealed class Registration(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
