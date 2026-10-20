using System.Text;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Tests;

/// <summary>Damaged or unusual project files load cleanly or fail with <see cref="DmxFileFormatException"/>.</summary>
public class ProjectSerializerRobustnessTests
{
    private readonly ProjectSerializer _serializer = new();

    [Fact]
    public void Deserialize_Utf8BomOnBytePath_Loads()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{"schemaVersion":1,"name":"Bom"}""")).ToArray();

        var project = _serializer.Deserialize(bytes.AsSpan()).Project;

        Assert.Equal("Bom", project.Name);
    }

    [Fact]
    public async Task LoadAsync_FileWithUtf8Bom_Loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmxpro-bom-{Guid.NewGuid():N}.dmxpro");
        try
        {
            await File.WriteAllTextAsync(path, """{"schemaVersion":1,"name":"Bom file"}""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var result = await _serializer.LoadAsync(path);

            Assert.Equal("Bom file", result.Project.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("""[1, 2]""")]
    [InlineData("""  "text"  """)]
    [InlineData("""null""")]
    [InlineData("""true""")]
    public void Deserialize_RootNotAnObject_ThrowsFileFormatException(string json)
    {
        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Theory]
    [InlineData("""{"schemaVersion":1} {}""")]
    [InlineData("""{"schemaVersion":1} x""")]
    [InlineData("""{"schemaVersion":1}]""")]
    public void Deserialize_TrailingGarbage_ThrowsFileFormatException(string json)
    {
        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_TrailingWhitespaceAndComment_Loads()
    {
        var project = _serializer.Deserialize("{\"schemaVersion\":1,\"name\":\"T\"}\n  // end\n").Project;

        Assert.Equal("T", project.Name);
    }

    [Fact]
    public void Deserialize_OnlyNestedSchemaVersion_ThrowsFileFormatException()
    {
        const string json = """{"stage":{"schemaVersion":1},"fixtures":[{"schemaVersion":1}]}""";

        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_NestedNewerSchemaVersion_IgnoredForTopLevelVersion()
    {
        const string json = """{"stage":{"schemaVersion":999},"schemaVersion":1,"name":"N"}""";

        Assert.Equal("N", _serializer.Deserialize(json).Project.Name);
    }

    [Fact]
    public void Deserialize_FunctionWithoutTypeDiscriminator_ThrowsFileFormatException()
    {
        const string json = """
            {"schemaVersion":1,"functions":[{"id":"6f1c3a52-6b0e-4a3e-9d07-3d9d1d0f0a11","name":"S"}]}
            """;

        var ex = Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
        Assert.StartsWith("Project file is damaged", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_FunctionWithUnknownTypeDiscriminator_ThrowsFileFormatException()
    {
        const string json = """
            {"schemaVersion":1,"functions":[{"type":"laser","id":"6f1c3a52-6b0e-4a3e-9d07-3d9d1d0f0a11","name":"S"}]}
            """;

        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_WrongValueType_ThrowsFileFormatException()
    {
        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize("""{"schemaVersion":1,"fixtures":{}}"""));
    }
}
