using System.Windows.Controls;
using System.Windows.Input;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Show transport bar of the Virtual Console. Code-behind only turns pointer positions into timeline fractions and
/// routes the bar's arrow keys; seeking and clamping live in <see cref="ShowPlayerViewModel"/>.
/// </summary>
public partial class ConsoleShowTransportBar : UserControl
{
    private const double SmallStepSeconds = 1;

    public ConsoleShowTransportBar()
    {
        InitializeComponent();
    }

    private ShowPlayerViewModel? Vm => DataContext as ShowPlayerViewModel;

    /// <summary>Left/Right = ∓ seek step (Shift: ∓1 s), Home = start — only while focus is inside the bar.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { HasActiveShow: true } vm)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return;
        }

        var step = modifiers == ModifierKeys.Shift ? SmallStepSeconds : vm.LargeStepSeconds;
        switch (e.Key)
        {
            case Key.Left:
                Seek(vm, -step, e.IsRepeat);
                e.Handled = true;
                break;
            case Key.Right:
                Seek(vm, step, e.IsRepeat);
                e.Handled = true;
                break;
            case Key.Home when modifiers == ModifierKeys.None:
                vm.SeekTo(0);
                e.Handled = true;
                break;
        }
    }

    /// <summary>A held arrow key (auto-repeat) is a fast seek: the music stays silent until the key is released.</summary>
    private static void Seek(ShowPlayerViewModel vm, double delta, bool isRepeat)
    {
        if (isRepeat)
        {
            vm.HoldSeekBy(delta);
        }
        else
        {
            vm.SeekBy(delta);
        }
    }

    /// <summary>Releasing an arrow key, or Space/Enter on a focused FF/REW button, ends a fast seek.</summary>
    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Space or Key.Enter)
        {
            Vm?.EndFastSeek();
        }
    }

    /// <summary>FF/REW button released: the music rejoins at once.</summary>
    private void OnFastSeekMouseUp(object sender, MouseButtonEventArgs e) => Vm?.EndFastSeek();

    /// <summary>FF/REW button lost the mouse (released elsewhere, window deactivated): end the hold too.</summary>
    private void OnFastSeekLostCapture(object sender, MouseEventArgs e) => Vm?.EndFastSeek();

    private void OnTimelineMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { HasActiveShow: true } vm)
        {
            return;
        }

        Timeline.Focus();
        vm.BeginScrub();
        vm.ScrubTo(FractionAt(e));
        Timeline.CaptureMouse();
        e.Handled = true;
    }

    private void OnTimelineMouseMove(object sender, MouseEventArgs e)
    {
        if (Timeline.IsMouseCaptured && Vm is { } vm)
        {
            vm.ScrubTo(FractionAt(e));
        }
    }

    private void OnTimelineMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Timeline.IsMouseCaptured)
        {
            Timeline.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnTimelineLostMouseCapture(object sender, MouseEventArgs e) => Vm?.EndScrub();

    private double FractionAt(MouseEventArgs e)
    {
        var width = TimelineSurface.ActualWidth;
        return width > 0 ? e.GetPosition(TimelineSurface).X / width : 0;
    }
}
