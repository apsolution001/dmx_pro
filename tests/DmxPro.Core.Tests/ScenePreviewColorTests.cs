using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class ScenePreviewColorTests
{
    private readonly Project _project = new();
    private readonly Fixture _left;
    private readonly Fixture _right;

    public ScenePreviewColorTests()
    {
        var fixtures = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "PAR");
        _left = fixtures[0];
        _right = fixtures[1];
    }

    [Fact]
    public void Estimate_RedChannelOnly_IsRed()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "r", 255);

        Assert.Equal(new RgbColor(255, 0, 0), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void Estimate_DimmerAtHalf_ScalesColour()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "b", 255);
        scene.SetValue(_left.Id, "dim", 128);

        Assert.Equal(new RgbColor(0, 0, 128), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void Estimate_WhiteChannel_AddsToAllComponents()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "r", 255);
        scene.SetValue(_left.Id, "w", 102);

        Assert.Equal(new RgbColor(255, 102, 102), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void Estimate_TwoFixtures_TakesBrightestPerComponent()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "r", 255);
        scene.SetValue(_right.Id, "g", 200);

        Assert.Equal(new RgbColor(255, 200, 0), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void Estimate_DimmerOnly_IsGrey()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "dim", 255);

        Assert.Equal(new RgbColor(255, 255, 255), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void Estimate_NoColourChannelsOrUnknownFixture_IsNull()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "strobe", 200);
        scene.SetValue(Guid.NewGuid(), "r", 255);

        Assert.Null(ScenePreviewColor.Estimate(scene, _project));
        Assert.Null(ScenePreviewColor.Estimate(new SceneFunction(), _project));
    }

    [Theory]
    [InlineData(255, 255, 255, true)]
    [InlineData(255, 255, 0, true)]
    [InlineData(0, 0, 255, false)]
    [InlineData(0, 0, 0, false)]
    public void IsLight_Luminance_PicksTextContrast(byte r, byte g, byte b, bool expected)
    {
        Assert.Equal(expected, new RgbColor(r, g, b).IsLight);
    }

    [Fact]
    public void ToHex_Colour_FormatsUppercaseHash()
    {
        Assert.Equal("#FF8000", new RgbColor(255, 128, 0).ToHex());
    }

    [Fact]
    public void Estimate_InterleavedFixtureValues_GroupsPerFixture()
    {
        // Values of one fixture split by another fixture's values must still mix together (dimmer scales its colour).
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "r", 255);
        scene.SetValue(_right.Id, "g", 64);
        scene.SetValue(_left.Id, "dim", 128);

        Assert.Equal(new RgbColor(128, 64, 0), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void Estimate_UnknownFixture_Ignored()
    {
        var scene = new SceneFunction();
        scene.SetValue(Guid.NewGuid(), "r", 255);
        scene.SetValue(_right.Id, "b", 200);

        Assert.Equal(new RgbColor(0, 0, 200), ScenePreviewColor.Estimate(scene, _project));
    }

    [Fact]
    public void PreviewColor_FromIndex_MatchesDirectRead()
    {
        var scene = new SceneFunction();
        scene.SetValue(_left.Id, "r", 200);
        scene.SetValue(_left.Id, "w", 40);
        scene.SetValue(_left.Id, "dim", 100);
        scene.SetValue(_right.Id, "g", 90);

        var index = scene.IndexValues();

        Assert.Equal(scene.PreviewColor(_project, _left), index.PreviewColor(_project, _left));
        Assert.Equal(scene.PreviewColor(_project, _right), index.PreviewColor(_project, _right));
    }

    [Fact]
    public void IndexValues_DuplicateEntries_FirstWinsLikeGetValue()
    {
        var scene = new SceneFunction();
        scene.Values.Add(new SceneValue { FixtureId = _left.Id, ChannelKey = "r", Value = 10 });
        scene.Values.Add(new SceneValue { FixtureId = _left.Id, ChannelKey = "r", Value = 20 });

        var index = scene.IndexValues();

        Assert.Equal(scene.GetValue(_left.Id, "r"), index[(_left.Id, "r")]);
        Assert.Single(index);
    }
}
