using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.Core.Projects;
using DmxPro.Engine;
using DmxPro.Output;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmxPro.App.Tests;

/// <summary>
/// The real App service graph (as wired in App.xaml.cs) built by hand for tests: no window, no Application, no audio
/// device and no output hardware (the output factory refuses to create anything, and output is never enabled). The
/// engine thread only runs when a test calls <see cref="StartEngine"/>. Dispose stops it again.
/// </summary>
internal sealed class AppRig : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"DmxPro.App.Tests-{Guid.NewGuid():N}.json");
    private bool _engineStarted;

    public AppRig()
    {
        Projects = new ProjectService(new ProjectSerializer(), NullLogger<ProjectService>.Instance);
        Engine = new DmxEngine();
        Playback = new EngineConsolePlayback(Engine, NullLogger<EngineConsolePlayback>.Instance);
        Output = new OutputManager(new NoHardwareOutputFactory());
        Host = new EngineHost(Engine);
        Music = new ShowMusicService(Projects, Playback, new FakeAudioAnalyzer(), () => new FakeAudioPlayer(), NullLogger<ShowMusicService>.Instance);
        Runtime = new EngineRuntimeService(
            Engine, Host, Output, new OutputFrameSink(Output), Projects, Playback, Music, NullLogger<EngineRuntimeService>.Instance);
        LiveMode = new LiveModeService(Engine, Playback, Runtime, NullLogger<LiveModeService>.Instance);
        Preview = new FunctionPreviewService(Engine, Projects, LiveMode, Playback, Runtime, Output, NullLogger<FunctionPreviewService>.Instance);
        Settings = new AppSettingsService(NullLogger<AppSettingsService>.Instance, _settingsPath);
    }

    public ProjectService Projects { get; }

    public DmxEngine Engine { get; }

    public EngineConsolePlayback Playback { get; }

    public OutputManager Output { get; }

    public EngineHost Host { get; }

    public ShowMusicService Music { get; }

    public EngineRuntimeService Runtime { get; }

    public LiveModeService LiveMode { get; }

    public FunctionPreviewService Preview { get; }

    public AppSettingsService Settings { get; }

    public FakeDialogService Dialogs { get; } = new();

    public LightClipboardService Clipboard { get; } = new();

    /// <summary>Starts the engine thread and subscribes the runtime to project changes (needed for ProjectLoaded).</summary>
    public void StartEngine()
    {
        _engineStarted = true;
        Runtime.Start();
    }

    public FunctionsViewModel CreateFunctionsViewModel()
    {
        var console = new VirtualConsoleViewModel(Projects, Playback, Dialogs, Settings, Music);
        return new FunctionsViewModel(Projects, Dialogs, Settings, Clipboard, console, Preview);
    }

    public void Dispose()
    {
        if (_engineStarted)
        {
            Runtime.ShutdownAsync().GetAwaiter().GetResult();
        }

        Playback.Dispose();
        try
        {
            File.Delete(_settingsPath);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
    }
}
