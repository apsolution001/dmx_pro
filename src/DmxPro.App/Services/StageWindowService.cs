using System.Windows;
using DmxPro.App.ViewModels;
using DmxPro.App.Views;

namespace DmxPro.App.Services;

/// <summary>
/// Keeps exactly one live stage window. While it is open it holds one viewer on the shared
/// <see cref="StagePageViewModel"/>, so the live preview keeps running when the Stage page is not shown.
/// </summary>
public sealed class StageWindowService(StagePageViewModel stage) : IStageWindowService
{
    private StageWindow? _window;

    /// <inheritdoc />
    public bool IsOpen => _window is not null;

    /// <inheritdoc />
    public void Show()
    {
        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var window = new StageWindow(stage);
        window.Closed += OnWindowClosed;
        _window = window;
        stage.AddViewer();
        try
        {
            window.Show();
        }
        catch
        {
            // Never shown, so Closed will not come: release the viewer here, then let the caller report the error.
            window.Closed -= OnWindowClosed;
            _window = null;
            stage.RemoveViewer();
            throw;
        }

        window.Activate();
    }

    /// <inheritdoc />
    public void Close() => _window?.Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is StageWindow window)
        {
            window.Closed -= OnWindowClosed;
        }

        _window = null;

        // A light dragged in the window is saved with the layout even when the Stage page still has a viewer.
        stage.CommitMove();
        stage.RemoveViewer();
    }
}
