using System.Collections.ObjectModel;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;

namespace DmxPro.App.Tests;

public sealed class ChaserStepListEditingTests
{
    [Fact]
    public void MoveBy_SingleUp_SwapsWithPrevious()
    {
        Assert.Equal([0, 2, 1, 3], ChaserStepListEditing.MoveBy(4, [2], -1)!);
    }

    [Fact]
    public void MoveBy_BlockDown_MovesTogether()
    {
        Assert.Equal([0, 3, 1, 2], ChaserStepListEditing.MoveBy(4, [1, 2], 1)!);
    }

    [Fact]
    public void MoveBy_ScatteredUp_EachMovesOnePlace()
    {
        Assert.Equal([1, 0, 3, 2], ChaserStepListEditing.MoveBy(4, [1, 3], -1)!);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(3, 1)]
    public void MoveBy_SelectionAtEnd_ReturnsNull(int index, int offset)
    {
        Assert.Null(ChaserStepListEditing.MoveBy(4, [index], offset));
    }

    [Fact]
    public void MoveBy_GroupTouchingTop_DoesNotMove()
    {
        Assert.Null(ChaserStepListEditing.MoveBy(4, [0, 2], -1));
    }

    [Fact]
    public void MoveBy_NothingOrEverythingSelected_ReturnsNull()
    {
        Assert.Null(ChaserStepListEditing.MoveBy(3, [], 1));
        Assert.Null(ChaserStepListEditing.MoveBy(3, [0, 1, 2], 1));
    }

    [Fact]
    public void MoveBy_InvalidIndexesIgnored()
    {
        Assert.Equal([1, 0, 2], ChaserStepListEditing.MoveBy(3, [1, 7, -1], -1)!);
    }

    [Fact]
    public void MoveTo_SingleToTop()
    {
        Assert.Equal([3, 0, 1, 2], ChaserStepListEditing.MoveTo(4, [3], 0)!);
    }

    [Fact]
    public void MoveTo_SingleToEnd()
    {
        Assert.Equal([1, 2, 3, 0], ChaserStepListEditing.MoveTo(4, [0], 4)!);
    }

    [Fact]
    public void MoveTo_ScatteredSelection_BecomesBlockInOrder()
    {
        // Steps 0 and 3 dropped into the gap before 2.
        Assert.Equal([1, 0, 3, 2, 4], ChaserStepListEditing.MoveTo(5, [3, 0], 2)!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MoveTo_OntoItsOwnPlace_ReturnsNull(int gap)
    {
        Assert.Null(ChaserStepListEditing.MoveTo(4, [1], gap));
    }

    [Fact]
    public void MoveTo_GapOutOfRange_Clamped()
    {
        Assert.Equal([1, 2, 0], ChaserStepListEditing.MoveTo(3, [0], 99)!);
    }

    [Fact]
    public void ApplyOrder_ReordersWithMoves()
    {
        var items = new ObservableCollection<string>(["a", "b", "c", "d"]);
        var moves = 0;
        items.CollectionChanged += (_, e) =>
        {
            Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Move, e.Action);
            moves++;
        };

        ChaserStepListEditing.ApplyOrder(items, [3, 0, 1, 2]);

        Assert.Equal(["d", "a", "b", "c"], items);
        Assert.True(moves > 0);
    }

    [Fact]
    public void ApplyOrder_WrongLength_Throws()
    {
        var items = new ObservableCollection<int>([1, 2]);
        Assert.Throws<ArgumentException>(() => ChaserStepListEditing.ApplyOrder(items, [0]));
    }

    [Fact]
    public void InsertIndex_AfterLastSelected_OrEnd()
    {
        Assert.Equal(3, ChaserStepListEditing.InsertIndex(5, [0, 2]));
        Assert.Equal(5, ChaserStepListEditing.InsertIndex(5, []));
    }

    [Fact]
    public void SelectionAfterRemove_PicksNextOrLast()
    {
        Assert.Equal(1, ChaserStepListEditing.SelectionAfterRemove(5, [1, 2]));
        Assert.Equal(2, ChaserStepListEditing.SelectionAfterRemove(4, [3]));
        Assert.Equal(-1, ChaserStepListEditing.SelectionAfterRemove(2, [0, 1]));
    }

    [Theory]
    [InlineData(RunOrder.Loop, RunDirection.Forward, new[] { 0, 1, 2, 3 })]
    [InlineData(RunOrder.SingleShot, RunDirection.Backward, new[] { 3, 2, 1, 0 })]
    [InlineData(RunOrder.PingPong, RunDirection.Forward, new[] { 0, 1, 2, 3, 2, 1 })]
    [InlineData(RunOrder.PingPong, RunDirection.Backward, new[] { 3, 2, 1, 0, 1, 2 })]
    public void PlayOrder_MatchesRunOrderAndDirection(RunOrder order, RunDirection direction, int[] expected)
    {
        Assert.Equal(expected, ChaserStepListEditing.PlayOrder(4, order, direction));
    }

    [Fact]
    public void PlayOrder_PingPongWithTwoSteps_DoesNotRepeat()
    {
        Assert.Equal([0, 1], ChaserStepListEditing.PlayOrder(2, RunOrder.PingPong, RunDirection.Forward));
        Assert.Empty(ChaserStepListEditing.PlayOrder(0, RunOrder.Loop, RunDirection.Forward));
    }

    [Fact]
    public void ApplyTiming_SetsClampsAndCountsChanges()
    {
        var same = new ChaserStep { HoldSeconds = 2, FadeInSeconds = 0.5, FadeOutSeconds = 0 };
        var other = new ChaserStep { HoldSeconds = 1 };

        var changed = ChaserStepListEditing.ApplyTiming([same, other], 2, 0.5, -3);

        Assert.Equal(1, changed);
        Assert.Equal(2, other.HoldSeconds);
        Assert.Equal(0.5, other.FadeInSeconds);
        Assert.Equal(0, other.FadeOutSeconds);
    }

    [Fact]
    public void ApplyTiming_RoundsAndCapsAtOneHour()
    {
        var step = new ChaserStep();

        ChaserStepListEditing.ApplyTiming([step], 99999, 0.12345, 1);

        Assert.Equal(FunctionEditing.MaxSeconds, step.HoldSeconds);
        Assert.Equal(0.123, step.FadeInSeconds);
    }
}
