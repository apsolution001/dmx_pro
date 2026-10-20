using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Tests;

public class ShowSerializationTests
{
    private readonly ProjectSerializer _serializer = new();

    [Fact]
    public void Deserialize_ProjectWithFunctionsAndShows_RoundTripsAllData()
    {
        var project = new Project();
        var fixture = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "PAR")[0];
        var red = new SceneFunction { Name = "Red", FadeInSeconds = 0.5, FadeOutSeconds = 1.5 };
        red.SetValue(fixture.Id, "r", 255);
        project.Functions.Add(red);
        var show = project.CreateShow("Wedding");
        show.Bpm = 128;
        show.LengthSeconds = 211;
        var item = project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, red.Id, 2, 4);
        item.SetTiming(2, 4, 0.25, 0.75);

        var loaded = _serializer.Deserialize(_serializer.Serialize(project));

        Assert.Empty(loaded.Project.ValidateFunctionsAndShows());
        var p = loaded.Project;
        Assert.Equal(2, p.Functions.Count);
        var loadedRed = Assert.IsType<SceneFunction>(p.FindFunction(red.Id));
        Assert.Equal("Red", loadedRed.Name);
        Assert.Equal(FunctionType.Scene, loadedRed.Type);
        Assert.Equal(0.5, loadedRed.FadeInSeconds);
        Assert.Equal(1.5, loadedRed.FadeOutSeconds);
        Assert.Equal((byte)255, loadedRed.GetValue(fixture.Id, "r"));
        Assert.Null(loadedRed.OwnerShowId);
        Assert.True(loadedRed.ShowInFunctionList);

        var loadedShow = Assert.Single(p.Shows);
        Assert.Equal("Wedding", loadedShow.Name);
        Assert.Equal(128, loadedShow.Bpm);
        Assert.Equal(211, loadedShow.LengthSeconds);
        var loadedItem = Assert.Single(Assert.Single(loadedShow.Tracks).Items);
        Assert.Equal(item.Id, loadedItem.Id);
        Assert.Equal(2, loadedItem.StartSeconds);
        Assert.Equal(4, loadedItem.LengthSeconds);
        Assert.Equal(0.25, loadedItem.FadeInSeconds);
        Assert.Equal(0.75, loadedItem.FadeOutSeconds);

        var copy = Assert.IsType<SceneFunction>(p.FindFunction(loadedItem.FunctionId));
        Assert.Equal(show.Id, copy.OwnerShowId);
        Assert.False(copy.ShowInFunctionList);
        Assert.Equal(red.Id, copy.CopiedFromId);
        Assert.Equal([red.Id], p.FunctionListItems.Select(f => f.Id));
    }

    [Fact]
    public void Serialize_SceneFunction_WritesTypeDiscriminatorAndChannelKey()
    {
        var project = new Project();
        var scene = new SceneFunction { Name = "Red" };
        scene.SetValue(Guid.NewGuid(), "r", 200);
        project.Functions.Add(scene);

        var json = _serializer.Serialize(project);

        // Exactly once: the discriminator only, not also the Type property.
        Assert.Equal(2, json.Split("\"type\": \"Scene\"").Length);
        Assert.Contains("\"channelKey\": \"r\"", json);
        Assert.DoesNotContain("allItems", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("functionListItems", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_OldProjectWithoutFunctionsOrShows_LoadsEmptyLists()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "id": "6f1f7a52-9a8e-4d55-9a32-8b3fd2a3f0d1",
              "name": "Old",
              "profiles": [],
              "fixtures": [],
              "groups": []
            }
            """;

        var result = _serializer.Deserialize(json);

        Assert.Empty(result.Project.Functions);
        Assert.Empty(result.Project.Shows);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "functions": [ { "name": "No type" } ] }""")]
    [InlineData("""{ "schemaVersion": 1, "functions": [ { "type": "Hologram", "name": "Future" } ] }""")]
    public void Deserialize_FunctionWithMissingOrUnknownType_ThrowsFormatException(string json)
    {
        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }
}
