using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Tests;

public sealed class ForwardingCommandTests
{
    [Fact]
    public void CanExecute_ResolverReturnsNull_False()
    {
        var command = new ForwardingCommand(() => null, null);

        Assert.False(command.CanExecute(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanExecute_ResolvedCommand_FollowsTarget(bool targetCanRun)
    {
        var target = new RelayCommand(() => { }, () => targetCanRun);
        var command = new ForwardingCommand(() => target, null, target);

        Assert.Equal(targetCanRun, command.CanExecute(null));
    }

    [Fact]
    public void CanExecuteChanged_WatchedCommandChanges_IsRaised()
    {
        var enabled = false;
        var target = new RelayCommand(() => { }, () => enabled);
        var command = new ForwardingCommand(() => target, null, target);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        enabled = true;
        target.NotifyCanExecuteChanged();

        Assert.Equal(1, raised);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void NotifyCanExecuteChanged_PageChanged_FollowsTheNewPagesCommand()
    {
        var pageA = new RelayCommand(() => { }, () => true);
        var pageB = new RelayCommand(() => { }, () => false);
        ICommand current = pageA;
        var command = new ForwardingCommand(() => current, null, pageA, pageB);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;
        var before = command.CanExecute(null);

        current = pageB;
        command.NotifyCanExecuteChanged();

        Assert.True(before);
        Assert.False(command.CanExecute(null));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Execute_TargetCanRun_RunsBeforeExecuteThenTargetWithParameter()
    {
        var calls = new List<string>();
        var target = new RelayCommand<object?>(p => calls.Add($"target:{p}"), _ => true);
        var command = new ForwardingCommand(() => target, () => calls.Add("navigate"), target);

        command.Execute("x");

        Assert.Equal(["navigate", "target:x"], calls);
    }

    [Fact]
    public void Execute_TargetCannotRun_DoesNothing()
    {
        var calls = new List<string>();
        var target = new RelayCommand(() => calls.Add("target"), () => false);
        var command = new ForwardingCommand(() => target, () => calls.Add("navigate"), target);

        command.Execute(null);

        Assert.Empty(calls);
    }

    [Fact]
    public void Execute_NavigationSwitchesToCommandThatCannotRun_TargetNotRun()
    {
        var ran = new List<string>();
        var first = new RelayCommand(() => ran.Add("first"), () => true);
        var second = new RelayCommand(() => ran.Add("second"), () => false);
        ICommand current = first;
        var command = new ForwardingCommand(() => current, () => current = second, first, second);

        command.Execute(null);

        Assert.Empty(ran);
    }

    [Fact]
    public void Execute_NavigationSwitchesToAnotherRunnableCommand_RunsTheNewOne()
    {
        var ran = new List<string>();
        var first = new RelayCommand(() => ran.Add("first"), () => true);
        var second = new RelayCommand(() => ran.Add("second"), () => true);
        ICommand current = first;
        var command = new ForwardingCommand(() => current, () => current = second, first, second);

        command.Execute(null);

        Assert.Equal(["second"], ran);
    }
}
