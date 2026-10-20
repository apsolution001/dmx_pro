using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Tests;

public class ProjectSerializerTests
{
    private readonly ProjectSerializer _serializer = new();

    private static Project SampleProject()
    {
        var project = new Project { Name = "Wedding" };
        project.EmbedProfile(TestProfiles.Rgbw());
        var fixture = new Fixture { Name = "Stage Left", ProfileId = "test-rgbw", ModeName = "8ch", StartAddress = 101, Notes = "truss" };
        project.Fixtures.Add(fixture);
        project.Groups.Add(new FixtureGroup { Name = "All PARs", FixtureIds = [fixture.Id] });
        return project;
    }

    [Fact]
    public void Deserialize_SerializedProject_RoundTripsAllData()
    {
        var original = SampleProject();

        var loaded = _serializer.Deserialize(_serializer.Serialize(original)).Project;

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("Wedding", loaded.Name);
        var fixture = Assert.Single(loaded.Fixtures);
        Assert.Equal(original.Fixtures[0].Id, fixture.Id);
        Assert.Equal(101, fixture.StartAddress);
        Assert.Equal("truss", fixture.Notes);
        Assert.Equal(fixture.Id, Assert.Single(Assert.Single(loaded.Groups).FixtureIds));
        Assert.Equal(8, loaded.FindProfile("test-rgbw")!.FindMode("8ch")!.ChannelCount);
    }

    [Fact]
    public void Serialize_Project_WritesEnumsAsStringsAndNoOutputState()
    {
        var json = _serializer.Serialize(SampleProject());

        Assert.Contains("\"type\": \"Red\"", json);
        Assert.DoesNotContain("enabledOutput", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outputEnabled", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_NewerSchema_Throws()
    {
        var ex = Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize("{\"schemaVersion\": 999}"));
        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"name\": \"no version\"}")]
    [InlineData("[1, 2]")]
    [InlineData("{\"schemaVersion\": \"1\"}")]
    [InlineData("{\"schemaVersion\": 1, \"name\": \"x\"} trailing")]
    [InlineData("{\"schemaVersion\": 1, \"fixtures\": [}")]
    [InlineData("")]
    public void Deserialize_InvalidFile_ThrowsFormatException(string json)
    {
        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_GroupWithDeletedFixture_ReportsWarning()
    {
        var project = SampleProject();
        project.Fixtures.Clear();

        var result = _serializer.Deserialize(_serializer.Serialize(project));

        Assert.Contains(result.Issues, i => i.Code == "GROUP_MISSING_FIXTURE");
    }

    [Fact]
    public async Task SaveAsync_ExistingFile_KeepsBackupAndReplaces()
    {
        var dir = Directory.CreateTempSubdirectory("dmxpro-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "show.dmxpro");
            var project = SampleProject();
            await _serializer.SaveAsync(project, path);
            project.Name = "Second";
            await _serializer.SaveAsync(project, path);

            Assert.Equal("Second", (await _serializer.LoadAsync(path)).Project.Name);
            Assert.True(File.Exists(path + ".bak"));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void RemoveFixture_InGroup_RemovesGroupReference()
    {
        var project = SampleProject();

        project.RemoveFixture(project.Fixtures[0].Id);

        Assert.Empty(project.Fixtures);
        Assert.Empty(project.Groups[0].FixtureIds);
    }

    [Fact]
    public async Task LoadAsync_FileWithUtf8Bom_Loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmxpro-bom-{Guid.NewGuid():N}.dmxpro");
        try
        {
            await File.WriteAllTextAsync(path, _serializer.Serialize(SampleProject()), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var loaded = (await _serializer.LoadAsync(path)).Project;

            Assert.Equal("Wedding", loaded.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Deserialize_CommentsAndTrailingCommas_Accepted()
    {
        var loaded = _serializer.Deserialize("{ /* a comment */ \"schemaVersion\": 1, \"name\": \"Hand edited\", }").Project;

        Assert.Equal("Hand edited", loaded.Name);
    }

    [Fact]
    public void Deserialize_SceneValues_ShareChannelKeyStrings()
    {
        var project = SampleProject();
        var fixture = project.Fixtures[0];
        var first = new DmxPro.Core.Functions.SceneFunction { Name = "A" };
        first.SetValue(fixture.Id, "r", 10);
        var second = new DmxPro.Core.Functions.SceneFunction { Name = "B" };
        second.SetValue(fixture.Id, "r", 20);
        project.Functions.Add(first);
        project.Functions.Add(second);

        var loaded = _serializer.Deserialize(_serializer.Serialize(project)).Project;

        var a = Assert.IsType<DmxPro.Core.Functions.SceneFunction>(loaded.Functions[0]).Values[0];
        var b = Assert.IsType<DmxPro.Core.Functions.SceneFunction>(loaded.Functions[1]).Values[0];
        Assert.Equal("r", a.ChannelKey);
        Assert.Equal(10, a.Value);
        Assert.Equal(20, b.Value);
        Assert.Same(a.ChannelKey, b.ChannelKey);
        Assert.Same(loaded.FindProfile("test-rgbw")!.FindChannel("r")!.Key, a.ChannelKey);
    }
}
