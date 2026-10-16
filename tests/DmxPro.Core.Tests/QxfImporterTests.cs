using System.Xml.Linq;
using DmxPro.Core.Profiles;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Tests;

public class QxfImporterTests
{
    private static FixtureProfile ImportSample() =>
        QxfImporter.ImportFile(Path.Combine(AppContext.BaseDirectory, "TestData", "Sample-Par-RGBW.qxf"));

    [Fact]
    public void ImportFile_Sample_ReadsIdentityAndIsUnverified()
    {
        var profile = ImportSample();

        Assert.Equal("sample-par-rgbw-7x10", profile.Id);
        Assert.Equal("Sample", profile.Manufacturer);
        Assert.Equal("PAR RGBW 7x10", profile.Model);
        Assert.False(profile.Verified);
        Assert.Contains("Apache-2.0", profile.Source);
        Assert.Contains("Test Author", profile.Source);
        Assert.Empty(profile.Validate());
    }

    [Fact]
    public void ImportFile_Sample_ClassifiesPresetsAndLegacyGroups()
    {
        var profile = ImportSample();

        Assert.Equal(ChannelType.Dimmer, profile.FindChannel("dimmer")!.Type);
        Assert.Equal(ChannelType.Red, profile.FindChannel("red")!.Type);
        Assert.Equal(ChannelType.Green, profile.FindChannel("green")!.Type); // <Group>Intensity</Group><Colour>Green</Colour>
        Assert.Equal(ChannelType.Strobe, profile.FindChannel("strobe")!.Type);
        Assert.Equal(2, profile.FindChannel("strobe")!.Capabilities.Count);
    }

    [Fact]
    public void ImportFile_FineChannel_LinksToCoarse()
    {
        var fine = ImportSample().FindChannel("dimmer-fine")!;

        Assert.Equal(ChannelType.Fine, fine.Type);
        Assert.Equal("dimmer", fine.CoarseKey);
    }

    [Fact]
    public void ImportFile_UnsupportedChannel_ImportedAsGenericWithNote()
    {
        var pan = ImportSample().FindChannel("pan")!;

        Assert.Equal(ChannelType.Generic, pan.Type);
        Assert.Contains("PositionPan", pan.Description);
    }

    [Fact]
    public void ImportFile_Modes_OrderedByChannelNumber()
    {
        var mode = ImportSample().FindMode("8 Channel")!;

        Assert.Equal(["dimmer", "dimmer-fine", "red", "green", "blue", "white", "strobe", "pan"], mode.Channels);
    }

    [Fact]
    public void Import_NotAFixtureDefinition_Throws()
    {
        Assert.Throws<DmxFileFormatException>(() => QxfImporter.Import(XDocument.Parse("<Workspace/>")));
    }

    [Theory]
    [InlineData("Stairville LED Par 56", "stairville-led-par-56")]
    [InlineData("  RGB/W -- Bar!! ", "rgb-w-bar")]
    public void Slug_Text_ProducesDashedLowercase(string input, string expected)
    {
        Assert.Equal(expected, QxfImporter.Slug(input));
    }
}
