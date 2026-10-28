using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.Audio.Analysis;
using DmxPro.Audio.Playback;
using DmxPro.Output;
using DmxPro.Output.Configuration;

namespace DmxPro.App.Tests;

/// <summary>Refuses to create any output: tests never touch network adapters or USB ports.</summary>
internal sealed class NoHardwareOutputFactory : IDmxOutputFactory
{
    public IDmxOutput Create(OutputConfig config) =>
        throw new InvalidOperationException("App tests must not create real outputs.");
}

internal sealed class FakeAudioAnalyzer : IAudioAnalyzer
{
    public Task<AudioAnalysis> AnalyzeAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("App tests must not analyse audio.");
}

/// <summary>Silent player (never opens an audio device).</summary>
internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public event EventHandler<AudioErrorEventArgs>? Error
    {
        add { }
        remove { }
    }

    public string? FilePath { get; private set; }

    public TimeSpan Duration => TimeSpan.Zero;

    public TimeSpan Position => TimeSpan.Zero;

    public bool IsPlaying => false;

    public float Volume { get; set; } = 1;

    public long MemoryBytes => 0;

    public Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        FilePath = path;
        return Task.CompletedTask;
    }

    public void Play()
    {
    }

    public void Pause()
    {
    }

    public void Stop()
    {
    }

    public void Seek(TimeSpan position)
    {
    }

    public void SetFadeOut(TimeSpan start, TimeSpan duration)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>Dialogs answered by the test; every prompt is recorded.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public SaveChangesChoice SaveChangesAnswer { get; set; } = SaveChangesChoice.Discard;

    public bool ConfirmAnswer { get; set; } = true;

    public List<string> Prompts { get; } = [];

    public ExternalChangeChoice ExternalChangeAnswer { get; set; } = ExternalChangeChoice.Reload;

    /// <summary>File names of the "changed on disk" prompts shown.</summary>
    public List<string> ExternalChangePrompts { get; } = [];

    public bool IsModalOpen { get; set; }

    public Task<ExternalChangeChoice> AskReloadExternalChangeAsync(string fileName)
    {
        ExternalChangePrompts.Add(fileName);
        return Task.FromResult(ExternalChangeAnswer);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        Prompts.Add(title);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        Prompts.Add(title);
        return Task.CompletedTask;
    }

    public Task<SaveChangesChoice> AskSaveChangesAsync(string projectName)
    {
        Prompts.Add(projectName);
        return Task.FromResult(SaveChangesAnswer);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Prompts.Add(title);
        return Task.FromResult(ConfirmAnswer);
    }

    public bool ShowAddFixture(AddFixtureViewModel viewModel) => throw new NotSupportedException();

    public bool ShowChannelLayout(ChannelLayoutViewModel viewModel) => throw new NotSupportedException();

    public bool ShowSaveFunction(SaveFunctionDialogViewModel viewModel) => throw new NotSupportedException();
}
