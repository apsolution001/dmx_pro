using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

public class ConsoleFunctionButtonTests
{
    private readonly Project _project = new();
    private readonly ConsolePage _page;
    private readonly SceneFunction _red;

    public ConsoleFunctionButtonTests()
    {
        _page = _project.AddConsolePage("Main");
        _red = _project.CreateScene("Red");
    }

    [Fact]
    public void AddFunctionButton_EmptyPage_AddsToggleButtonTopLeft()
    {
        var widget = _project.AddFunctionButton(_page.Id, _red.Id);

        Assert.NotNull(widget);
        Assert.Same(widget, Assert.Single(_page.Widgets));
        Assert.Equal(_red.Id, widget.FunctionId);
        Assert.Equal("Red", widget.Label);
        Assert.Equal(FunctionWidgetMode.Toggle, widget.Mode);
        Assert.Equal((0, 0), (widget.X, widget.Y));
        Assert.Equal((ConsoleWidget.DefaultWidth, ConsoleWidget.DefaultHeight), (widget.Width, widget.Height));
    }

    [Fact]
    public void AddFunctionButton_SpotTaken_UsesNextFreeSpot()
    {
        _page.Widgets.Add(new BlackoutWidget { X = 0, Y = 0 });

        var widget = _project.AddFunctionButton(_page.Id, _red.Id)!;

        Assert.Equal((2, 0), (widget.X, widget.Y));
    }

    [Fact]
    public void AddFunctionButton_PageFull_ReturnsNullAndAddsNothing()
    {
        _page.Columns = 2;
        _page.Rows = 2;
        _page.Widgets.Add(new BlackoutWidget { X = 0, Y = 0 });

        Assert.Null(_project.AddFunctionButton(_page.Id, _red.Id));
        Assert.Single(_page.Widgets);
    }

    [Fact]
    public void AddFunctionButton_UnknownFunction_Throws()
    {
        Assert.Throws<ArgumentException>(() => _project.AddFunctionButton(_page.Id, Guid.NewGuid()));
    }

    [Fact]
    public void AddFunctionButton_UnknownPage_Throws()
    {
        Assert.Throws<ArgumentException>(() => _project.AddFunctionButton(Guid.NewGuid(), _red.Id));
    }
}
