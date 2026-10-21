using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class FunctionClipboardTests
{
    private readonly Project _project = new();
    private readonly FunctionClipboard _clipboard = new();

    private SceneFunction AddScene(string name, byte red = 255)
    {
        var scene = new SceneFunction { Name = name };
        scene.SetValue(Guid.NewGuid(), "red", red);
        _project.Functions.Add(scene);
        return scene;
    }

    [Fact]
    public void PasteInto_Empty_ReturnsNullAndAddsNothing()
    {
        Assert.False(_clipboard.HasFunction);
        Assert.Null(_clipboard.PasteInto(_project));
        Assert.Empty(_project.Functions);
    }

    [Fact]
    public void PasteInto_CopiedScene_AddsIndependentCopyNamedCopy()
    {
        var red = AddScene("Red");
        _clipboard.Copy(red);

        var pasted = Assert.IsType<SceneFunction>(_clipboard.PasteInto(_project));

        Assert.Equal("Red (copy)", pasted.Name);
        Assert.NotEqual(red.Id, pasted.Id);
        Assert.Equal(red.Id, pasted.CopiedFromId);
        Assert.Same(pasted, _project.Functions[^1]);
        Assert.Equal(255, pasted.Values[0].Value);
        Assert.NotSame(red.Values[0], pasted.Values[0]);
    }

    [Fact]
    public void PasteInto_Twice_GivesUniqueNamesAndIds()
    {
        _clipboard.Copy(AddScene("Red"));

        var first = _clipboard.PasteInto(_project)!;
        var second = _clipboard.PasteInto(_project)!;

        Assert.Equal("Red (copy)", first.Name);
        Assert.Equal("Red (copy 2)", second.Name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(3, _project.Functions.Count);
    }

    [Fact]
    public void PasteInto_SourceChangedAfterCopy_PastesValuesFromCopyTime()
    {
        var red = AddScene("Red", red: 200);
        _clipboard.Copy(red);
        red.Name = "Renamed";
        red.Values[0].Value = 10;

        var pasted = (SceneFunction)_clipboard.PasteInto(_project)!;

        Assert.Equal("Red (copy)", pasted.Name);
        Assert.Equal(200, pasted.Values[0].Value);
    }

    [Fact]
    public void PasteInto_SourceDeletedAfterCopy_StillPastes()
    {
        var red = AddScene("Red");
        _clipboard.Copy(red);
        _project.DeleteFunction(red.Id);

        var pasted = _clipboard.PasteInto(_project);

        Assert.NotNull(pasted);
        Assert.Equal("Red (copy)", pasted.Name);
        Assert.Same(pasted, Assert.Single(_project.Functions));
    }

    [Fact]
    public void PasteInto_ShowCopy_PastesListedLibraryFunction()
    {
        var showCopy = AddScene("Intro look");
        showCopy.OwnerShowId = Guid.NewGuid();
        showCopy.ShowInFunctionList = false;
        _clipboard.Copy(showCopy);

        var pasted = _clipboard.PasteInto(_project)!;

        Assert.Null(pasted.OwnerShowId);
        Assert.True(pasted.ShowInFunctionList);
        Assert.Contains(pasted, _project.FunctionListItems);
    }

    [Fact]
    public void PasteInto_Chaser_KeepsStepReferences()
    {
        var red = AddScene("Red");
        var chaser = new ChaserFunction { Name = "Run" };
        chaser.Steps.Add(new ChaserStep { FunctionId = red.Id });
        _project.Functions.Add(chaser);
        _clipboard.Copy(chaser);

        var pasted = (ChaserFunction)_clipboard.PasteInto(_project)!;

        Assert.Equal(red.Id, Assert.Single(pasted.Steps).FunctionId);
        Assert.NotSame(chaser.Steps[0], pasted.Steps[0]);
    }

    [Fact]
    public void Copy_RaisesChangedAndReportsName()
    {
        var raised = 0;
        _clipboard.Changed += (_, _) => raised++;

        _clipboard.Copy(AddScene("Red"));

        Assert.Equal(1, raised);
        Assert.True(_clipboard.HasFunction);
        Assert.Equal("Red", _clipboard.FunctionName);
    }

    [Fact]
    public void Clear_WithContent_EmptiesAndRaisesChangedOnce()
    {
        _clipboard.Copy(AddScene("Red"));
        var raised = 0;
        _clipboard.Changed += (_, _) => raised++;

        _clipboard.Clear();
        _clipboard.Clear();

        Assert.False(_clipboard.HasFunction);
        Assert.Null(_clipboard.FunctionName);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void PasteName_CopyNameTakenCaseInsensitive_SkipsIt()
    {
        AddScene("red (COPY)");

        Assert.Equal("Red (copy 2)", FunctionClipboard.PasteName(_project, "Red"));
    }
}
