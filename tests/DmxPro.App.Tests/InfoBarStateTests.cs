using DmxPro.App.Services;
using Wpf.Ui.Controls;

namespace DmxPro.App.Tests;

public sealed class InfoBarStateTests : IDisposable
{
    private static readonly TimeSpan Duration = InfoBarState.SuccessMessageDuration;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly ManualTimeProvider _time = new();
    private readonly InfoBarState _bar;

    public InfoBarStateTests()
    {
        _bar = new InfoBarState(_time);
    }

    public void Dispose() => _bar.Dispose();

    [Fact]
    public void New_Default_IsClosedAndInformational()
    {
        Assert.Null(_bar.Message);
        Assert.False(_bar.IsOpen);
        Assert.Equal(InfoBarSeverity.Informational, _bar.Severity);
    }

    [Fact]
    public void Show_Success_OpensBarWithMessageAndSeverity()
    {
        Show(InfoBarSeverity.Success, "Saved.");

        Assert.Equal("Saved.", _bar.Message);
        Assert.Equal(InfoBarSeverity.Success, _bar.Severity);
        Assert.True(_bar.IsOpen);
    }

    [Fact]
    public void Show_Success_HidesAfterDuration()
    {
        Show(InfoBarSeverity.Success, "Saved.");

        _time.Advance(Duration - Tick);
        Assert.Equal("Saved.", _bar.Message);
        Assert.True(_bar.IsOpen);

        _time.Advance(Tick);
        Assert.Null(_bar.Message);
        Assert.False(_bar.IsOpen);
    }

    [Theory]
    [InlineData(InfoBarSeverity.Warning)]
    [InlineData(InfoBarSeverity.Error)]
    [InlineData(InfoBarSeverity.Informational)]
    public void Show_NonSuccess_StaysUntilClosed(InfoBarSeverity severity)
    {
        Show(severity, "Check this.");

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal("Check this.", _bar.Message);
        Assert.Equal(severity, _bar.Severity);
        Assert.True(_bar.IsOpen);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void Show_SuccessWithoutAutoHide_StaysUntilClosed()
    {
        Show(InfoBarSeverity.Success, "Imported 3 profiles.", autoHide: false);

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal("Imported 3 profiles.", _bar.Message);
        Assert.True(_bar.IsOpen);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void Show_WarningWhileSuccessTimerPending_TimerNeverHidesWarning()
    {
        Show(InfoBarSeverity.Success, "Saved.");
        _time.Advance(Duration - Tick);

        Show(InfoBarSeverity.Warning, "Profile missing.");
        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal("Profile missing.", _bar.Message);
        Assert.Equal(InfoBarSeverity.Warning, _bar.Severity);
        Assert.True(_bar.IsOpen);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void Show_WarningAfterTimerFiredButBeforeCallbackRan_WarningStays()
    {
        // The real timer fires on a pool thread and posts to the UI thread; a warning can land in between.
        var context = new QueuedSynchronizationContext();
        RunWithContext(context, () => _bar.Show(InfoBarSeverity.Success, "Saved."));
        _time.Advance(Duration);
        Assert.Equal(1, context.PendingCount);

        Show(InfoBarSeverity.Warning, "Profile missing.");
        context.RunAll();

        Assert.Equal("Profile missing.", _bar.Message);
        Assert.True(_bar.IsOpen);
    }

    [Fact]
    public void Show_SuccessWithContext_HidesOnlyWhenPostedCallbackRuns()
    {
        var context = new QueuedSynchronizationContext();
        RunWithContext(context, () => _bar.Show(InfoBarSeverity.Success, "Saved."));

        _time.Advance(Duration);
        Assert.Equal("Saved.", _bar.Message);

        context.RunAll();
        Assert.Null(_bar.Message);
        Assert.False(_bar.IsOpen);
    }

    [Fact]
    public void Show_NewerSuccess_RestartsTimer()
    {
        Show(InfoBarSeverity.Success, "Saved.");
        _time.Advance(Duration - Tick);

        Show(InfoBarSeverity.Success, "Opened.");
        _time.Advance(Tick);
        Assert.Equal("Opened.", _bar.Message);

        _time.Advance(Duration - Tick - Tick);
        Assert.Equal("Opened.", _bar.Message);

        _time.Advance(Tick);
        Assert.Null(_bar.Message);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void Show_IdenticalMessage_ReshowsBarAndRaisesChanges()
    {
        Show(InfoBarSeverity.Success, "Saved.");
        var messages = new List<string?>();
        var openStates = new List<bool>();
        _bar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InfoBarState.Message))
            {
                messages.Add(_bar.Message);
            }
            else if (e.PropertyName == nameof(InfoBarState.IsOpen))
            {
                openStates.Add(_bar.IsOpen);
            }
        };

        Show(InfoBarSeverity.Success, "Saved.");

        Assert.Equal(new string?[] { null, "Saved." }, messages);
        Assert.Equal(new[] { false, true }, openStates);
        Assert.True(_bar.IsOpen);
    }

    [Fact]
    public void Show_IdenticalSuccess_RestartsTimer()
    {
        Show(InfoBarSeverity.Success, "Saved.");
        _time.Advance(Duration - Tick);

        Show(InfoBarSeverity.Success, "Saved.");
        _time.Advance(Duration - Tick);
        Assert.Equal("Saved.", _bar.Message);

        _time.Advance(Tick);
        Assert.Null(_bar.Message);
    }

    [Fact]
    public void Show_IdenticalMessageAfterClose_ReopensBar()
    {
        Show(InfoBarSeverity.Warning, "Profile missing.");
        _bar.IsOpen = false;

        Show(InfoBarSeverity.Warning, "Profile missing.");

        Assert.True(_bar.IsOpen);
        Assert.Equal("Profile missing.", _bar.Message);
    }

    [Fact]
    public void IsOpen_SetFalse_ClearsMessageAndCancelsTimer()
    {
        Show(InfoBarSeverity.Success, "Saved.");

        _bar.IsOpen = false;

        Assert.Null(_bar.Message);
        Assert.False(_bar.IsOpen);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void IsOpen_ClosedThenWarningShown_OldTimerDoesNotHideIt()
    {
        Show(InfoBarSeverity.Success, "Saved.");
        _bar.IsOpen = false;

        Show(InfoBarSeverity.Error, "Save failed.");
        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal("Save failed.", _bar.Message);
        Assert.True(_bar.IsOpen);
    }

    [Fact]
    public void Show_NullMessage_HidesBarAndCancelsTimer()
    {
        Show(InfoBarSeverity.Success, "Saved.");

        Show(InfoBarSeverity.Informational, null);

        Assert.Null(_bar.Message);
        Assert.False(_bar.IsOpen);
        Assert.Equal(InfoBarSeverity.Informational, _bar.Severity);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void Show_NullSuccessMessage_StartsNoTimer()
    {
        Show(InfoBarSeverity.Success, null);

        Assert.False(_bar.IsOpen);
        Assert.Equal(0, _time.ActiveTimerCount);
    }

    [Fact]
    public void Show_SeverityRaisedBeforeMessage_BoundBarNeverShowsNewTextWithOldSeverity()
    {
        Show(InfoBarSeverity.Success, "Saved.");
        var order = new List<string?>();
        _bar.PropertyChanged += (_, e) => order.Add(e.PropertyName);

        Show(InfoBarSeverity.Error, "Save failed.");

        Assert.Equal(
            new[] { nameof(InfoBarState.Severity), nameof(InfoBarState.Message) },
            order.Where(n => n != nameof(InfoBarState.IsOpen)));
    }

    [Fact]
    public void Dispose_PendingSuccess_CancelsTimer()
    {
        Show(InfoBarSeverity.Success, "Saved.");

        _bar.Dispose();

        Assert.Equal(0, _time.ActiveTimerCount);
    }

    /// <summary>
    /// Shows with no synchronization context (xUnit may install its own), so the fake timer hides the bar inline,
    /// inside <see cref="ManualTimeProvider.Advance"/>.
    /// </summary>
    private void Show(InfoBarSeverity severity, string? message, bool autoHide = true) =>
        RunWithContext(null, () => _bar.Show(severity, message, autoHide));

    private static void RunWithContext(SynchronizationContext? context, Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Queues posted callbacks until the test runs them, like a busy UI thread.</summary>
    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PendingCount => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void RunAll()
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}
