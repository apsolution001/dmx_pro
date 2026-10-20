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
    public void ImportFile_SampleStrobeChannel_NoFunctionRangeMarkedSafe()
    {
        var strobe = ImportSample().FindChannel("strobe")!;

        Assert.True(strobe.Capabilities[0].Safe);   // 0–9 "No function"
        Assert.Null(strobe.Capabilities[0].Preset);
        Assert.False(strobe.Capabilities[1].Safe);  // 10–255 strobe
        Assert.Equal("StrobeSlowToFast", strobe.Capabilities[1].Preset);
        Assert.Equal(StrobeRangeKind.Strobing, StrobeCapabilityClassifier.Classify(strobe.Capabilities[1]));
    }

    [Fact]
    public void Import_ShutterCapabilityPresets_MarksOnlyOpenRangeSafe()
    {
        var document = XDocument.Parse("""
            <FixtureDefinition xmlns="http://www.qlcplus.org/FixtureDefinition">
             <Manufacturer>M</Manufacturer>
             <Model>Shutter test</Model>
             <Channel Name="Shutter" Preset="ShutterStrobeSlowFast">
              <Capability Min="0" Max="9" Preset="ShutterClose">Blackout</Capability>
              <Capability Min="10" Max="19" Preset="ShutterOpen">LED on</Capability>
              <Capability Min="20" Max="200" Preset="StrobeRandom">Random</Capability>
              <Capability Min="201" Max="255">Sound active</Capability>
             </Channel>
             <Channel Name="Macro" Preset="ColorMacro">
              <Capability Min="0" Max="9">No function</Capability>
             </Channel>
             <Mode Name="2ch"><Channel Number="0">Shutter</Channel><Channel Number="1">Macro</Channel></Mode>
            </FixtureDefinition>
            """);

        var profile = QxfImporter.Import(document);
        var shutter = profile.FindChannel("shutter")!;

        Assert.Equal(ChannelType.Strobe, shutter.Type);
        Assert.Equal([false, true, false, false], shutter.Capabilities.Select(c => c.Safe));
        Assert.Equal(["ShutterClose", "ShutterOpen", "StrobeRandom", null], shutter.Capabilities.Select(c => c.Preset));
        Assert.False(profile.FindChannel("macro")!.Capabilities[0].Safe); // only strobe channels get the flag
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
