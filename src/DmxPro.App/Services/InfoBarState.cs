using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace DmxPro.App.Services;

/// <summary>
/// State and rules of the shell info bar. Success messages hide themselves after
/// <see cref="SuccessMessageDuration"/> (unless shown with <c>autoHide: false</c>); warnings and errors stay until the
/// user closes them (safety rule 10). A newer message cancels the older one's timer, an identical repeat shows the bar
/// again, and closing the bar forgets the message.
/// </summary>
/// <remarks>
/// Only <see cref="Show"/> (plus its own auto-hide / close handling) writes <see cref="Message"/> and
/// <see cref="Severity"/>, so a pending success timer can never wipe a later warning. Call it on the UI thread: the
/// auto-hide comes back to the synchronization context that was current when the message was shown.
/// </remarks>
public sealed partial class InfoBarState : ObservableObject, IDisposable
{
    /// <summary>How long a success message (Saved / Opened …) stays in the info bar.</summary>
    public static readonly TimeSpan SuccessMessageDuration = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _time;

    /// <summary>Pending auto-hide of the current success message; a newer message or closing the bar cancels it.</summary>
    private ITimer? _autoHide;

    /// <summary>Identifies the pending auto-hide, so a callback that was already queued when it got cancelled does nothing.</summary>
    private long _autoHideId;

    /// <summary>Message of the info bar; null hides it.</summary>
    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;

    /// <summary>
    /// Whether the bar is open. Bound two-way to the bar: its close button sets this to false, which clears the
    /// message, so the same message shown again (a second save) opens the bar again.
    /// </summary>
    [ObservableProperty]
    private bool _isOpen;

    public InfoBarState(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Shows a message (null hides the bar). The bar always reopens, also for a message identical to the current one.
    /// Success messages hide themselves after <see cref="SuccessMessageDuration"/> unless <paramref name="autoHide"/>
    /// is false; warnings and errors stay until the user closes them (safety rule 10).
    /// </summary>
    public void Show(InfoBarSeverity severity, string? message, bool autoHide = true)
    {
        CancelAutoHide();

        // An identical message would not raise a change: clear first so the bar shows (and restarts its timer) again.
        if (message is not null && message == Message)
        {
            Message = null;
        }

        Severity = severity;
        Message = message;
        if (message is not null && autoHide && severity == InfoBarSeverity.Success)
        {
            StartAutoHide();
        }
    }

    /// <inheritdoc />
    public void Dispose() => CancelAutoHide();

    private void StartAutoHide()
    {
        var id = ++_autoHideId;
        var context = SynchronizationContext.Current;
        _autoHide = _time.CreateTimer(
            _ =>
            {
                if (context is null)
                {
                    HideIfCurrent(id);
                }
                else
                {
                    context.Post(_ => HideIfCurrent(id), null);
                }
            },
            null,
            SuccessMessageDuration,
            Timeout.InfiniteTimeSpan);
    }

    private void HideIfCurrent(long id)
    {
        // Cancelled (newer message / closed) after the timer fired but before this ran: leave the bar alone.
        if (_autoHide is null || id != _autoHideId)
        {
            return;
        }

        CancelAutoHide();
        Message = null;
    }

    private void CancelAutoHide()
    {
        _autoHide?.Dispose();
        _autoHide = null;
    }

    partial void OnMessageChanged(string? value) => IsOpen = value is not null;

    partial void OnIsOpenChanged(bool value)
    {
        // Closed with the bar's close button: forget the message (and its timer).
        if (!value && Message is not null)
        {
            CancelAutoHide();
            Message = null;
        }
    }
}
