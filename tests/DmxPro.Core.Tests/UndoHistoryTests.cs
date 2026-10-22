using DmxPro.Core.History;

namespace DmxPro.Core.Tests;

public class UndoHistoryTests
{
    private static UndoHistory<string> Started(int maxSteps = 100)
    {
        var history = new UndoHistory<string>(maxSteps);
        history.Reset("s0");
        return history;
    }

    [Fact]
    public void Reset_NewHistory_NothingToUndoOrRedoAndSaved()
    {
        var history = Started();

        Assert.Equal("s0", history.Current);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.True(history.IsAtSavedState);
        Assert.Null(history.UndoDescription);
    }

    [Fact]
    public void Commit_ThenUndo_ReturnsPreviousStateAndDescription()
    {
        var history = Started();
        history.Commit("s1", "Add fixture");

        Assert.True(history.CanUndo);
        Assert.Equal("Add fixture", history.UndoDescription);
        Assert.False(history.IsAtSavedState);

        Assert.Equal("s0", history.Undo());
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal("Add fixture", history.RedoDescription);
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void Redo_AfterUndo_ReturnsNextState()
    {
        var history = Started();
        history.Commit("s1", "a");
        history.Commit("s2", "b");
        history.Undo();
        history.Undo();

        Assert.Equal("s1", history.Redo());
        Assert.Equal("s2", history.Redo());
        Assert.Null(history.Redo());
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void Commit_AfterUndo_ClearsRedo()
    {
        var history = Started();
        history.Commit("s1", "a");
        history.Commit("s2", "b");
        history.Undo();

        history.Commit("x2", "c");

        Assert.False(history.CanRedo);
        Assert.Equal("c", history.UndoDescription);
        Assert.Equal("s1", history.Undo());
    }

    [Fact]
    public void Undo_NothingToUndo_ReturnsNull()
    {
        var history = Started();

        Assert.Null(history.Undo());
        Assert.Equal("s0", history.Current);
    }

    [Fact]
    public void Commit_PastCap_DropsOldestStep()
    {
        var history = Started(maxSteps: 3);
        for (var i = 1; i <= 5; i++)
        {
            history.Commit($"s{i}", $"step {i}");
        }

        Assert.Equal(3, history.UndoCount);
        Assert.Equal("s4", history.Undo());
        Assert.Equal("s3", history.Undo());
        Assert.Equal("s2", history.Undo());
        Assert.Null(history.Undo());
    }

    [Fact]
    public void Commit_SavedStateDroppedByCap_NeverSavedAgain()
    {
        var history = Started(maxSteps: 2);
        history.Commit("s1", "a");
        history.Commit("s2", "b");
        history.Commit("s3", "c");

        while (history.Undo() is not null)
        {
            Assert.False(history.IsAtSavedState);
        }

        Assert.Equal("s1", history.Current);
        Assert.False(history.IsAtSavedState);
    }

    [Fact]
    public void MarkSaved_ThenUndoAndRedo_SavedOnlyAtThatState()
    {
        var history = Started();
        history.Commit("s1", "a");
        history.MarkSaved();
        history.Commit("s2", "b");

        Assert.False(history.IsAtSavedState);
        history.Undo();
        Assert.True(history.IsAtSavedState);
        history.Undo();
        Assert.False(history.IsAtSavedState);
        history.Redo();
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void Commit_SavedStateOnDroppedRedoBranch_NeverSavedAgain()
    {
        var history = Started();
        history.Commit("s1", "a");
        history.MarkSaved();
        history.Undo();

        history.Commit("x1", "b");
        history.Undo();

        Assert.Equal("s0", history.Current);
        Assert.False(history.IsAtSavedState);
        history.Redo();
        Assert.False(history.IsAtSavedState);
    }

    [Fact]
    public void ReplaceCurrent_KeepsStepsAndSavedMarker()
    {
        var history = Started();
        history.Commit("s1", "a");
        history.MarkSaved();

        history.ReplaceCurrent("s1b");

        Assert.Equal("s1b", history.Current);
        Assert.True(history.IsAtSavedState);
        Assert.Equal("a", history.UndoDescription);
    }

    [Fact]
    public void Commit_BlankDescription_UsesChange()
    {
        var history = Started();
        history.Commit("s1", " ");

        Assert.Equal("Change", history.UndoDescription);
    }

    [Fact]
    public void Commit_BeforeReset_Throws()
    {
        var history = new UndoHistory<string>();

        Assert.Throws<InvalidOperationException>(() => history.Commit("s1", "a"));
    }
}
