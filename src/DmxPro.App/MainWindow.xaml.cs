using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly EngineRuntimeService _runtime;
    private readonly IStageWindowService _stageWindow;
    private bool _closeConfirmed;
    private bool _shuttingDown;

    // Window placement saved when entering full screen, restored when leaving it.
    private Rect _boundsBeforeFullScreen;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;
    private ResizeMode _resizeModeBeforeFullScreen = ResizeMode.CanResize;

    public MainWindow(ShellViewModel viewModel, EngineRuntimeService runtime, IStageWindowService stageWindow)
    {
        _viewModel = viewModel;
        _runtime = runtime;
        _stageWindow = stageWindow;
        DataContext = viewModel;
        InitializeComponent();
        FitToWorkArea();

        _viewModel.CloseRequested += (_, _) => Close();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed)
        {
            return;
        }

        // Closing must wait for the async "save changes?" prompt and the safe shutdown, so cancel now and close again
        // afterwards.
        e.Cancel = true;
        if (_shuttingDown)
        {
            return;
        }

        if (await _viewModel.ConfirmDiscardChangesAsync())
        {
            // The live stage window has no Owner (it must not float over this window), so close it here: it stops its
            // preview and commits a pending drag before the shutdown, and the app does not linger with it open.
            _stageWindow.Close();

            // Safety rule 6: stop functions → safe frame → stop output → stop engine, without blocking the UI thread.
            _shuttingDown = true;
            await _runtime.ShutdownAsync();
            _closeConfirmed = true;

            // With nothing to save the prompt completes synchronously, i.e. we are still inside this Closing event;
            // WPF forbids Close() there, so close again once the current close has finished.
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Maximized from outside while in full screen (Win+Up, taskbar menu): leave full screen and stay maximized,
        // so the window ends up in a normal maximized state that respects the taskbar.
        if (_viewModel.IsFullScreen && WindowState == WindowState.Maximized)
        {
            _stateBeforeFullScreen = WindowState.Maximized;
            _viewModel.IsFullScreen = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsFullScreen))
        {
            if (_viewModel.IsFullScreen)
            {
                EnterFullScreen();
            }
            else
            {
                LeaveFullScreen();
            }
        }
    }

    /// <summary>
    /// Full screen is a pure view concern. A maximized FluentWindow stops at the taskbar (work area), so instead the
    /// window goes to Normal state, loses its title bar, resize border and rounded corners, and is sized to the full
    /// bounds of its monitor. Windows then treats it as a full-screen app and keeps the taskbar behind it.
    /// </summary>
    private void EnterFullScreen()
    {
        _stateBeforeFullScreen = WindowState;
        _boundsBeforeFullScreen = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _resizeModeBeforeFullScreen = ResizeMode;

        var monitor = GetMonitorBounds();
        AppTitleBar.Visibility = Visibility.Collapsed;
        WindowCornerPreference = WindowCornerPreference.DoNotRound;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;
        Left = monitor.Left;
        Top = monitor.Top;
        Width = monitor.Width;
        Height = monitor.Height;
        Activate();
    }

    private void LeaveFullScreen()
    {
        AppTitleBar.Visibility = Visibility.Visible;
        WindowCornerPreference = WindowCornerPreference.Round;
        ResizeMode = _resizeModeBeforeFullScreen;

        if (!_boundsBeforeFullScreen.IsEmpty)
        {
            Left = _boundsBeforeFullScreen.Left;
            Top = _boundsBeforeFullScreen.Top;
            Width = _boundsBeforeFullScreen.Width;
            Height = _boundsBeforeFullScreen.Height;
        }

        WindowState = _stateBeforeFullScreen;
    }

    /// <summary>
    /// The design size (1440×900) is larger than small or scaled screens (e.g. 1920×1080 at 125 % = 1536×816 work
    /// area); shrink it so the title bar and status bar start on screen. CenterScreen then centres the result.
    /// </summary>
    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));
    }

    /// <summary>Bounds of the monitor the window is on, in device-independent units.</summary>
    private Rect GetMonitorBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var pixels = new Rect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);
        var toDips = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        return toDips is { } transform ? Rect.Transform(pixels, transform) : pixels;
    }

    private static class NativeMethods
    {
        public const int MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
        }
    }
}
