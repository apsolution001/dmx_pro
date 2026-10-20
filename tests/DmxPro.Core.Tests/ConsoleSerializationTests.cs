using System.Text.Json;
using System.Text.Json.Nodes;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

public class ConsoleSerializationTests
{
    private static List<ConsolePage> RoundTrip(List<ConsolePage> pages) =>
        JsonSerializer.Deserialize<List<ConsolePage>>(JsonSerializer.Serialize(pages, DmxJson.Options), DmxJson.Options)!;

    private static ConsolePage PageWithEveryWidget() => new()
    {
        Name = "Live",
        Columns = 12,
        Rows = 8,
        ShowButtonsAsSliders = true,
        Widgets =
        [
            new FunctionWidget
            {
                FunctionId = Guid.NewGuid(),
                Mode = FunctionWidgetMode.Flash,
                Label = "Red",
                Color = "#FF0000",
                X = 1,
                Y = 2,
                Width = 3,
                Height = 1,
                KeyShortcut = "Ctrl+1",
            },
            new ShowWidget { ShowId = Guid.NewGuid(), Label = "Wedding" },
            new GroupLevelWidget { GroupId = Guid.NewGuid(), Label = "Front" },
            new MasterWidget { Label = "Master" },
            new BlackoutWidget { Label = "Blackout" },
            new ReleaseAllWidget { Label = "Release All" },
            new SpeedWidget { FunctionId = Guid.NewGuid(), Label = "Speed" },
            new SpeedWidget { Label = "All speed" },
            new LabelWidget { Label = "Colors" },
        ],
    };

    [Fact]
    public void Serialize_EveryWidgetType_RoundTripsTypesAndData()
    {
        var page = PageWithEveryWidget();

        var loaded = Assert.Single(RoundTrip([page]));

        Assert.Equal(
            (page.Id, "Live", 12, 8, true),
            (loaded.Id, loaded.Name, loaded.Columns, loaded.Rows, loaded.ShowButtonsAsSliders));
        Assert.Equal(page.Widgets.Count, loaded.Widgets.Count);
        for (var i = 0; i < page.Widgets.Count; i++)
        {
            var expected = page.Widgets[i];
            var actual = loaded.Widgets[i];
            Assert.IsType(expected.GetType(), actual);
            Assert.Equal(
                (expected.Id, expected.Label, expected.Color, expected.X, expected.Y, expected.Width, expected.Height, expected.KeyShortcut),
                (actual.Id, actual.Label, actual.Color, actual.X, actual.Y, actual.Width, actual.Height, actual.KeyShortcut));
        }

        var function = Assert.IsType<FunctionWidget>(loaded.Widgets[0]);
        Assert.Equal(((FunctionWidget)page.Widgets[0]).FunctionId, function.FunctionId);
        Assert.Equal(FunctionWidgetMode.Flash, function.Mode);
        Assert.Equal(((ShowWidget)page.Widgets[1]).ShowId, Assert.IsType<ShowWidget>(loaded.Widgets[1]).ShowId);
        Assert.Equal(((GroupLevelWidget)page.Widgets[2]).GroupId, Assert.IsType<GroupLevelWidget>(loaded.Widgets[2]).GroupId);
        Assert.Equal(((SpeedWidget)page.Widgets[6]).FunctionId, Assert.IsType<SpeedWidget>(loaded.Widgets[6]).FunctionId);
        Assert.Null(Assert.IsType<SpeedWidget>(loaded.Widgets[7]).FunctionId);
    }

    [Fact]
    public void Serialize_Widgets_WritesTypeDiscriminatorAndStringEnums()
    {
        var json = JsonSerializer.Serialize(PageWithEveryWidget().Widgets, DmxJson.Options);

        using var doc = JsonDocument.Parse(json);
        var types = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Equal(["Function", "Show", "GroupLevel", "Master", "Blackout", "ReleaseAll", "Speed", "Speed", "Label"], types);
        Assert.Equal("Flash", doc.RootElement[0].GetProperty("mode").GetString());
    }

    [Fact]
    public void Deserialize_WidgetWithoutSize_UsesTypeDefaults()
    {
        const string json = """[{ "type": "Master", "label": "M" }, { "type": "Blackout" }]""";

        var widgets = JsonSerializer.Deserialize<List<ConsoleWidget>>(json, DmxJson.Options)!;

        Assert.Equal((2, 4), (widgets[0].Width, widgets[0].Height));
        Assert.Equal((2, 2), (widgets[1].Width, widgets[1].Height));
    }

    [Fact]
    public void Deserialize_ProjectWithoutConsolePages_LoadsEmptyList()
    {
        var serializer = new ProjectSerializer();
        var node = JsonNode.Parse(serializer.Serialize(new Project { Name = "Old" }))!.AsObject();
        Assert.True(node.Remove("consolePages"));

        var loaded = serializer.Deserialize(node.ToJsonString()).Project;

        Assert.Equal("Old", loaded.Name);
        Assert.Empty(loaded.ConsolePages);
    }

    [Fact]
    public void Deserialize_ProjectWithConsole_RoundTripsPages()
    {
        var serializer = new ProjectSerializer();
        var project = new Project();
        var page = project.CreateDefaultConsolePage();
        page.ShowButtonsAsSliders = true;

        var loaded = serializer.Deserialize(serializer.Serialize(project)).Project;

        var loadedPage = Assert.Single(loaded.ConsolePages);
        Assert.True(loadedPage.ShowButtonsAsSliders);
        Assert.Equal(
            page.Widgets.Select(w => (w.GetType(), w.Id)),
            loadedPage.Widgets.Select(w => (w.GetType(), w.Id)));
    }
}
