using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.Validation;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

public class ConsoleLayoutTests
{
    private readonly Project _project = new();
    private readonly ConsolePage _page;

    public ConsoleLayoutTests()
    {
        _page = _project.AddConsolePage("Colors");
    }

    private SceneFunction AddScene(string name)
    {
        var scene = new SceneFunction { Name = name };
        _project.Functions.Add(scene);
        return scene;
    }

    [Fact]
    public void AddConsolePage_Name_AppendsEmptyPageWithDefaultGrid()
    {
        Assert.Same(_page, Assert.Single(_project.ConsolePages));
        Assert.Equal("Colors", _page.Name);
        Assert.Equal(ConsolePage.DefaultColumns, _page.Columns);
        Assert.Equal(ConsolePage.DefaultRows, _page.Rows);
        Assert.False(_page.ShowButtonsAsSliders);
        Assert.Empty(_page.Widgets);
        Assert.Same(_page, _project.FindConsolePage(_page.Id));
    }

    [Fact]
    public void RenameConsolePage_ExistingPage_ChangesName()
    {
        _project.RenameConsolePage(_page.Id, "Party");

        Assert.Equal("Party", _page.Name);
    }

    [Fact]
    public void RemoveConsolePage_ExistingAndMissing_ReturnsWhetherRemoved()
    {
        Assert.False(_project.RemoveConsolePage(Guid.NewGuid()));
        Assert.True(_project.RemoveConsolePage(_page.Id));
        Assert.Empty(_project.ConsolePages);
    }

    [Fact]
    public void AddConsoleWidget_InsidePage_KeepsGeometry()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new BlackoutWidget { X = 3, Y = 4, Width = 2, Height = 1 });

        Assert.Same(widget, Assert.Single(_page.Widgets));
        Assert.Equal((3, 4, 2, 1), (widget.X, widget.Y, widget.Width, widget.Height));
    }

    [Fact]
    public void AddConsoleWidget_OutsidePage_ClampsSizeThenPosition()
    {
        var widget = _project.AddConsoleWidget(
            _page.Id,
            new LabelWidget { X = 100, Y = -5, Width = 50, Height = 0 });

        Assert.Equal((0, 0, ConsolePage.DefaultColumns, 1), (widget.X, widget.Y, widget.Width, widget.Height));
        Assert.True(_page.Contains(widget));
    }

    [Fact]
    public void AddConsoleWidget_SameWidgetTwice_Throws()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new MasterWidget());

        Assert.Throws<ArgumentException>(() => _project.AddConsoleWidget(_page.Id, widget));
    }

    [Fact]
    public void AddConsoleWidget_MissingPage_Throws()
    {
        Assert.Throws<ArgumentException>(() => _project.AddConsoleWidget(Guid.NewGuid(), new MasterWidget()));
    }

    [Theory]
    [InlineData(2.4, 3.6, 2, 4)]
    [InlineData(2.5, 0.49, 3, 0)]
    [InlineData(-3, -0.4, 0, 0)]
    [InlineData(99, 99, ConsolePage.DefaultColumns - 2, ConsolePage.DefaultRows - 2)]
    public void MoveConsoleWidget_FractionalOrOutside_SnapsAndClamps(double x, double y, int expectedX, int expectedY)
    {
        var widget = _project.AddConsoleWidget(_page.Id, new BlackoutWidget());

        _project.MoveConsoleWidget(_page.Id, widget.Id, x, y);

        Assert.Equal((expectedX, expectedY), (widget.X, widget.Y));
        Assert.Equal((2, 2), (widget.Width, widget.Height));
    }

    [Fact]
    public void MoveConsoleWidget_NotFiniteCoordinate_Throws()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new BlackoutWidget());

        Assert.Throws<ArgumentOutOfRangeException>(() => _project.MoveConsoleWidget(_page.Id, widget.Id, double.NaN, 0));
    }

    [Fact]
    public void ResizeConsoleWidget_Fractional_SnapsToWholeCells()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new MasterWidget { X = 1, Y = 1 });

        _project.ResizeConsoleWidget(_page.Id, widget.Id, 3.4, 5.5);

        Assert.Equal((1, 1, 3, 6), (widget.X, widget.Y, widget.Width, widget.Height));
    }

    [Fact]
    public void ResizeConsoleWidget_BelowOneCell_KeepsMinimumOneByOne()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new MasterWidget());

        _project.ResizeConsoleWidget(_page.Id, widget.Id, 0.2, -4);

        Assert.Equal((1, 1), (widget.Width, widget.Height));
    }

    [Fact]
    public void ResizeConsoleWidget_PastPageEdge_StopsAtEdgeWithoutMoving()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new BlackoutWidget { X = 20, Y = 14 });

        _project.ResizeConsoleWidget(_page.Id, widget.Id, 10, 10);

        Assert.Equal((20, 14, 4, 2), (widget.X, widget.Y, widget.Width, widget.Height));
    }

    [Fact]
    public void DuplicateConsoleWidget_FunctionWidget_CopiesTargetIntoNextFreeSlot()
    {
        var red = AddScene("Red");
        var source = _project.AddConsoleWidget(_page.Id, new FunctionWidget
        {
            FunctionId = red.Id,
            Mode = FunctionWidgetMode.Flash,
            Label = "Red",
            Color = "#FF0000",
            KeyShortcut = "R",
        });

        var copy = Assert.IsType<FunctionWidget>(_project.DuplicateConsoleWidget(_page.Id, source.Id));

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(red.Id, copy.FunctionId);
        Assert.Equal(FunctionWidgetMode.Flash, copy.Mode);
        Assert.Equal("Red", copy.Label);
        Assert.Equal("#FF0000", copy.Color);
        Assert.Null(copy.KeyShortcut);
        Assert.Equal((2, 0, 2, 2), (copy.X, copy.Y, copy.Width, copy.Height));
        Assert.Equal([source, copy], _page.Widgets);
    }

    [Fact]
    public void DuplicateConsoleWidget_PageFull_PlacesCopyOnTopOfOriginal()
    {
        var source = _project.AddConsoleWidget(
            _page.Id,
            new LabelWidget { X = 0, Y = 0, Width = _page.Columns, Height = _page.Rows });

        var copy = _project.DuplicateConsoleWidget(_page.Id, source.Id);

        Assert.Equal((0, 0), (copy.X, copy.Y));
        Assert.Equal(2, _page.Widgets.Count);
    }

    [Fact]
    public void RemoveConsoleWidget_ExistingAndMissing_ReturnsWhetherRemoved()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new ReleaseAllWidget());

        Assert.False(_project.RemoveConsoleWidget(_page.Id, Guid.NewGuid()));
        Assert.True(_project.RemoveConsoleWidget(_page.Id, widget.Id));
        Assert.Empty(_page.Widgets);
    }

    [Fact]
    public void FindFreeSlot_OccupiedStart_ReturnsFirstGapRowByRow()
    {
        _page.Columns = 4;
        _page.Rows = 4;
        _project.AddConsoleWidget(_page.Id, new BlackoutWidget { X = 0, Y = 0 });
        _project.AddConsoleWidget(_page.Id, new ReleaseAllWidget { X = 2, Y = 0 });

        Assert.Equal((0, 2), _page.FindFreeSlot(2, 2));
        Assert.Equal((0, 2), _page.FindFreeSlot(4, 2));
        Assert.Null(_page.FindFreeSlot(4, 3));
    }

    [Fact]
    public void FindFreeSlot_IgnoredWidget_TreatsItsCellsAsFree()
    {
        var widget = _project.AddConsoleWidget(_page.Id, new BlackoutWidget());

        Assert.Equal((0, 0), _page.FindFreeSlot(2, 2, ignore: widget));
    }

    [Fact]
    public void ShowButtonsAsSliders_Toggled_SwapsFunctionButtonsAndKeepsTheirData()
    {
        var red = AddScene("Red");
        var button = _project.AddConsoleWidget(_page.Id, new FunctionWidget
        {
            FunctionId = red.Id,
            Mode = FunctionWidgetMode.Flash,
            Label = "Red",
            Color = "#FF0000",
            X = 4,
            Y = 2,
        });
        var blackout = _project.AddConsoleWidget(_page.Id, new BlackoutWidget());
        var show = _project.AddConsoleWidget(_page.Id, new ShowWidget());

        _page.ShowButtonsAsSliders = true;

        Assert.Equal(WidgetPresentation.Slider, _page.PresentationOf(button));
        Assert.Equal(WidgetPresentation.Button, _page.PresentationOf(blackout));
        Assert.Equal(WidgetPresentation.Button, _page.PresentationOf(show));
        var widget = Assert.IsType<FunctionWidget>(_page.FindWidget(button.Id));
        Assert.Equal((red.Id, "Red", "#FF0000", 4, 2), (widget.FunctionId, widget.Label, widget.Color, widget.X, widget.Y));

        _page.ShowButtonsAsSliders = false;

        Assert.Equal(WidgetPresentation.Button, _page.PresentationOf(button));
        Assert.Equal(FunctionWidgetMode.Flash, widget.Mode);
    }

    [Fact]
    public void PresentationOf_FixedWidgets_AreSlidersLabelsOrButtons()
    {
        _page.ShowButtonsAsSliders = true;

        Assert.Equal(WidgetPresentation.Slider, _page.PresentationOf(new MasterWidget()));
        Assert.Equal(WidgetPresentation.Slider, _page.PresentationOf(new GroupLevelWidget()));
        Assert.Equal(WidgetPresentation.Slider, _page.PresentationOf(new SpeedWidget()));
        Assert.Equal(WidgetPresentation.Label, _page.PresentationOf(new LabelWidget()));
        Assert.Equal(WidgetPresentation.Button, _page.PresentationOf(new ReleaseAllWidget()));
    }

    [Fact]
    public void CreateDefaultConsolePage_NoFunctions_HasOnlyTopRow()
    {
        var page = _project.CreateDefaultConsolePage();

        Assert.Equal("Main", page.Name);
        Assert.Collection(
            page.Widgets,
            w => Assert.IsType<BlackoutWidget>(w),
            w => Assert.IsType<ReleaseAllWidget>(w),
            w => Assert.IsType<MasterWidget>(w));
        Assert.All(page.Widgets, w => Assert.Equal(0, w.Y));
        Assert.Empty(_project.ValidateConsole());
    }

    [Fact]
    public void CreateDefaultConsolePage_SomeColorFunctions_AddsButtonsInSpecOrder()
    {
        var blue = AddScene("blue");
        var red = AddScene(" Red ");
        AddScene("Purple");
        var showCopy = AddScene("Green");
        showCopy.OwnerShowId = Guid.NewGuid();

        var page = _project.CreateDefaultConsolePage("Start");

        var colors = page.Widgets.OfType<FunctionWidget>().ToList();
        Assert.Equal(2, colors.Count);
        Assert.Equal((red.Id, "Red", "#FF0000", 0, 2), (colors[0].FunctionId, colors[0].Label, colors[0].Color, colors[0].X, colors[0].Y));
        Assert.Equal((blue.Id, "Blue", "#0000FF", 2, 2), (colors[1].FunctionId, colors[1].Label, colors[1].Color, colors[1].X, colors[1].Y));
        Assert.All(colors, c => Assert.Equal(FunctionWidgetMode.Toggle, c.Mode));
        Assert.All(page.Widgets, w => Assert.True(page.Contains(w)));
    }

    [Fact]
    public void ValidateConsole_MissingTargets_ReportsWarnings()
    {
        var red = AddScene("Red");
        var ok = _project.AddConsoleWidget(_page.Id, new FunctionWidget { FunctionId = red.Id });
        var missingFunction = _project.AddConsoleWidget(_page.Id, new FunctionWidget { FunctionId = Guid.NewGuid() });
        var missingShow = _project.AddConsoleWidget(_page.Id, new ShowWidget { ShowId = Guid.NewGuid() });
        var missingGroup = _project.AddConsoleWidget(_page.Id, new GroupLevelWidget { GroupId = Guid.NewGuid() });
        var missingSpeed = _project.AddConsoleWidget(_page.Id, new SpeedWidget { FunctionId = Guid.NewGuid() });
        var unassigned = _project.AddConsoleWidget(_page.Id, new ShowWidget());
        _project.AddConsoleWidget(_page.Id, new SpeedWidget());

        var issues = _project.ValidateConsole();

        Assert.All(issues, i => Assert.Equal(ValidationSeverity.Warning, i.Severity));
        Assert.Equal(
            [missingFunction.Id, missingShow.Id, missingGroup.Id, missingSpeed.Id],
            issues.Where(i => i.Code == "CONSOLE_WIDGET_MISSING_TARGET").Select(i => i.EntityId!.Value));
        Assert.Equal(unassigned.Id, Assert.Single(issues, i => i.Code == "CONSOLE_WIDGET_UNASSIGNED").EntityId);
        Assert.DoesNotContain(issues, i => i.EntityId == ok.Id);
        Assert.Equal(5, issues.Count);
    }

    [Fact]
    public void ValidateConsole_ExistingTargets_ReportsNothing()
    {
        var red = AddScene("Red");
        var show = _project.CreateShow("Wedding");
        var group = _project.CreateGroup("Front", []);
        _project.AddConsoleWidget(_page.Id, new FunctionWidget { FunctionId = red.Id });
        _project.AddConsoleWidget(_page.Id, new ShowWidget { ShowId = show.Id });
        _project.AddConsoleWidget(_page.Id, new GroupLevelWidget { GroupId = group.Id });
        _project.AddConsoleWidget(_page.Id, new SpeedWidget { FunctionId = red.Id });

        Assert.Empty(_project.ValidateConsole());
    }

    [Fact]
    public void ValidateConsole_WidgetOutsidePage_ReportsWarning()
    {
        var widget = new LabelWidget { X = 23, Y = 0, Width = 2 };
        _page.Widgets.Add(widget);

        var issue = Assert.Single(_project.ValidateConsole());

        Assert.Equal("CONSOLE_WIDGET_OUT_OF_PAGE", issue.Code);
        Assert.Equal(widget.Id, issue.EntityId);
    }

    [Fact]
    public void ValidateConsole_SameShortcutTwiceOnPage_ReportsWarning()
    {
        _project.AddConsoleWidget(_page.Id, new BlackoutWidget { KeyShortcut = "Ctrl+B" });
        _project.AddConsoleWidget(_page.Id, new LabelWidget { KeyShortcut = "ctrl+b " });
        var other = _project.AddConsolePage("Other");
        _project.AddConsoleWidget(other.Id, new ReleaseAllWidget { KeyShortcut = "Ctrl+B" });

        var issue = Assert.Single(_project.ValidateConsole());

        Assert.Equal("CONSOLE_DUPLICATE_SHORTCUT", issue.Code);
        Assert.Equal(_page.Id, issue.EntityId);
    }
}
