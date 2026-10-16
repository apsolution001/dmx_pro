using System.Windows;
using System.Windows.Threading;
using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.Core.Projects;
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

            services.AddSingleton<ProfilesViewModel>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<MainWindow>();
        })
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        await _host.StartAsync();

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
        // Phase 7: stop functions, send the configured safe frame, then stop outputs (safety rule 6).
        await _host.StopAsync(TimeSpan.FromSeconds(2));
        _host.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host.Services.GetRequiredService<ILogger<App>>().LogError(e.Exception, "Unhandled UI exception");
        System.Windows.MessageBox.Show(e.Exception.Message, "DMX Pro — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
