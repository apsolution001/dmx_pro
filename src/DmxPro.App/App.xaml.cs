using System.Windows;
using System.Windows.Threading;
using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.Core.Projects;
using DmxPro.Engine;
using DmxPro.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DmxPro.App;

public partial class App : Application
{
    private readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<AppPaths>();
            services.AddSingleton<ProjectSerializer>();
            services.AddSingleton<ProjectService>();
            services.AddSingleton<ProfileLibraryService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IThemeService, ThemeService>();

            services.AddSingleton<FixturesViewModel>();
            services.AddSingleton<FunctionsViewModel>();
            services.AddSingleton<ShowsViewModel>();

            // ---- Engine + output runtime. Output always starts DISABLED (safety rule 1); only the Input/Output page
            // enables it. OutputManager is IAsyncDisposable only, so the host must be disposed asynchronously.
            services.AddSingleton(sp => new DmxEngine(sp.GetRequiredService<ILogger<DmxEngine>>()));
            services.AddSingleton(sp => new EngineHost(sp.GetRequiredService<DmxEngine>(), sp.GetRequiredService<ILogger<EngineHost>>()));
            services.AddSingleton<IDmxOutputFactory, DmxOutputFactory>();
            services.AddSingleton(sp => new OutputManager(sp.GetRequiredService<IDmxOutputFactory>()));
            services.AddSingleton<OutputFrameSink>();
            services.AddSingleton<EngineConsolePlayback>();
            services.AddSingleton<IConsolePlayback>(sp => sp.GetRequiredService<EngineConsolePlayback>());
            services.AddSingleton<EngineRuntimeService>();

            // ---- Settings + Input/Output. ----
            services.AddSingleton(sp => new AppSettingsService(sp.GetRequiredService<ILogger<AppSettingsService>>()));
            services.AddSingleton<SettingsEngineBridge>();
            services.AddSingleton<IFolderPickerService, FolderPickerService>();
            services.AddSingleton<InputOutputViewModel>();
            services.AddSingleton<SettingsViewModel>();

            services.AddSingleton<LightClipboardService>();

            // ---- Music in shows: analysis (cached on disk), one player per running show, sync with the lights. ----
            services.AddSingleton<DmxPro.Audio.Analysis.IAudioAnalyzer>(sp =>
                new DmxPro.Audio.Analysis.AudioAnalyzer(sp.GetRequiredService<AppPaths>().AudioCacheDirectory));
            services.AddSingleton<Func<DmxPro.Audio.Playback.IAudioPlayer>>(_ => () => new DmxPro.Audio.Playback.AudioPlayer());
            services.AddSingleton<ShowMusicService>();
            services.AddSingleton<IShowMusicService>(sp => sp.GetRequiredService<ShowMusicService>());
            services.AddSingleton<VirtualConsoleViewModel>();
            services.AddSingleton(sp => new DmxPro.Engine.Testing.ChannelTestSession(sp.GetRequiredService<DmxEngine>()));
            services.AddSingleton<TestPageViewModel>();
            services.AddSingleton<StagePageViewModel>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<MainWindow>();
        })
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ChainImplicitStylesToWpfUi(Resources);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        await _host.StartAsync();
        _host.Services.GetRequiredService<EngineRuntimeService>().Start();
        _host.Services.GetRequiredService<SettingsEngineBridge>().Start();

        // Subscribes to show transport events, so it must exist before any show plays.
        _host.Services.GetRequiredService<ShowMusicService>();

        var libraryIssues = await _host.Services.GetRequiredService<ProfileLibraryService>().ReloadAsync();
        var shell = _host.Services.GetRequiredService<ShellViewModel>();
        shell.ReportLibraryIssues(libraryIssues);
        shell.CurrentPage.OnNavigatedTo();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // The main window already ran the safety-rule-6 close sequence; this is a no-op then and a best effort for any
        // other exit path (the shutdown task is shared, so it never runs twice).
        await _host.Services.GetRequiredService<EngineRuntimeService>().ShutdownAsync();
        await _host.StopAsync(TimeSpan.FromSeconds(2));
        if (_host is IAsyncDisposable asyncHost)
        {
            await asyncHost.DisposeAsync();
        }
        else
        {
            _host.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Gives the compact implicit styles of App.xaml WPF-UI's style for the same type as their base. XAML can't do this
    /// inside App.xaml: BasedOn="{StaticResource {x:Type T}}" resolves to the style being defined and ends up null,
    /// which left e.g. the TitleBar without a template (crash in its window hook). Styles are sealed once read from a
    /// dictionary, so each one is rebuilt with the base and put back. Runs before any window uses the styles.
    /// </summary>
    private static void ChainImplicitStylesToWpfUi(ResourceDictionary resources)
    {
        var wpfUi = resources.MergedDictionaries.OfType<Wpf.Ui.Markup.ControlsDictionary>().FirstOrDefault();
        if (wpfUi is null)
        {
            return;
        }

        foreach (var type in resources.Keys.OfType<Type>().ToList())
        {
            if (resources[type] is not Style { BasedOn: null } style || wpfUi[type] is not Style baseStyle
                || ReferenceEquals(baseStyle, style))
            {
                continue;
            }

            var rebuilt = new Style(style.TargetType ?? type, baseStyle) { Resources = style.Resources };
            foreach (var setter in style.Setters)
            {
                rebuilt.Setters.Add(setter switch
                {
                    Setter s => new Setter(s.Property, s.Value, s.TargetName),
                    _ => setter,
                });
            }

            foreach (var trigger in style.Triggers)
            {
                rebuilt.Triggers.Add(trigger);
            }

            resources[type] = rebuilt;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host.Services.GetRequiredService<ILogger<App>>().LogError(e.Exception, "Unhandled UI exception");
        System.Windows.MessageBox.Show(e.Exception.Message, "DMX Pro — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
