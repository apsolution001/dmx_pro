using System.Text.Json;
using DmxPro.Core.Validation;
using DmxPro.Output.Configuration;

namespace DmxPro.Output.Tests;

public class OutputConfigTests
{
    [Fact]
    public void Validate_DefaultConfigs_HaveNoErrors()
    {
        OutputConfig[] configs =
        [
            new VirtualOutputConfig(),
            new ArtNetOutputConfig(),
            new SacnOutputConfig(),
            new EnttecProOutputConfig { PortName = "COM3" },
            new OpenDmxOutputConfig(),
        ];

        foreach (var config in configs)
        {
            Assert.DoesNotContain(config.Validate(), i => i.Severity == ValidationSeverity.Error);
        }
    }

    [Theory]
    [InlineData(new int[0], "OUTPUT_NO_UNIVERSES")]
    [InlineData(new[] { 0 }, "OUTPUT_UNIVERSE_RANGE")]
    [InlineData(new[] { 64000 }, "OUTPUT_UNIVERSE_RANGE")]
    [InlineData(new[] { 1, 1 }, "OUTPUT_UNIVERSE_DUPLICATE")]
    public void Validate_BadUniverses_ReportsError(int[] universes, string code)
    {
        var config = new VirtualOutputConfig { Universes = [.. universes] };

        AssertError(config, code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    public void Validate_FrameRateOutOfRange_ReportsError(int rate)
    {
        AssertError(new VirtualOutputConfig { FrameRateHz = rate }, "OUTPUT_FRAME_RATE_RANGE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("300.1.1.1")]
    [InlineData("node.local")]
    [InlineData("::1")]
    public void Validate_ArtNetBadDestination_ReportsError(string destination)
    {
        AssertError(new ArtNetOutputConfig { Destination = destination }, "ARTNET_DESTINATION_INVALID");
    }

    [Fact]
    public void Validate_ArtNetBadBindAddress_ReportsError()
    {
        AssertError(new ArtNetOutputConfig { BindAddress = "abc" }, "OUTPUT_BIND_ADDRESS_INVALID");
    }

    [Fact]
    public void Validate_ArtNetNetSubNetUniverseOutOfRange_ReportsErrors()
    {
        var issues = new ArtNetOutputConfig { Net = 128, SubNet = 16, Universe = -1 }.Validate();

        Assert.Contains(issues, i => i.Code == "ARTNET_NET_RANGE");
        Assert.Contains(issues, i => i.Code == "ARTNET_SUBNET_RANGE");
        Assert.Contains(issues, i => i.Code == "ARTNET_UNIVERSE_RANGE");
    }

    [Fact]
    public void Validate_ArtNetUniverseBeyondPortAddressSpace_ReportsError()
    {
        var config = new ArtNetOutputConfig { Net = 127, SubNet = 15, Universe = 15, Universes = [1, 2] };

        var issues = config.Validate();

        var issue = Assert.Single(issues, i => i.Code == "ARTNET_PORT_ADDRESS_RANGE");
        Assert.Contains("universe 2", issue.Message);
        Assert.Equal(config.Id, issue.EntityId);
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(63999, 1)]
    public void Validate_SacnUniverseMapsOutsideRange_ReportsError(int universe, int offset)
    {
        AssertError(new SacnOutputConfig { Universes = [universe], UniverseOffset = offset }, "SACN_UNIVERSE_RANGE");
    }

    [Fact]
    public void Validate_SacnBadSettings_ReportsErrors()
    {
        var config = new SacnOutputConfig
        {
            Priority = 201,
            Cid = Guid.Empty,
            SourceName = " ",
            UseMulticast = false,
            UnicastAddress = null,
        };

        var issues = config.Validate();

        Assert.Contains(issues, i => i.Code == "SACN_PRIORITY_RANGE");
        Assert.Contains(issues, i => i.Code == "SACN_CID_EMPTY");
        Assert.Contains(issues, i => i.Code == "SACN_SOURCE_NAME_EMPTY");
        Assert.Contains(issues, i => i.Code == "SACN_UNICAST_ADDRESS_INVALID");
    }

    [Fact]
    public void Validate_SacnLongSourceName_IsOnlyWarning()
    {
        var issues = new SacnOutputConfig { SourceName = new string('x', 80) }.Validate();

        var issue = Assert.Single(issues);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void Validate_EnttecWithoutPortOrWithTwoUniverses_ReportsErrors()
    {
        var issues = new EnttecProOutputConfig { PortName = "", Universes = [1, 2] }.Validate();

        Assert.Contains(issues, i => i.Code == "SERIAL_PORT_MISSING");
        Assert.Contains(issues, i => i.Code == "USB_SINGLE_UNIVERSE");
    }

    [Fact]
    public void ManagerValidate_NoOutputs_ReportsError()
    {
        var issue = Assert.Single(OutputManager.Validate([]));

        Assert.Equal("OUTPUT_NONE_CONFIGURED", issue.Code);
    }

    [Fact]
    public void ManagerValidate_TwoOutputsOnSameComPort_ReportsError()
    {
        var issues = OutputManager.Validate(
        [
            new EnttecProOutputConfig { PortName = "COM3" },
            new EnttecProOutputConfig { PortName = "com3 ", Universes = [2] },
        ]);

        Assert.Contains(issues, i => i.Code == "OUTPUT_DEVICE_DUPLICATE");
    }

    [Fact]
    public void Serialize_Configs_RoundTripsWithTypeAndNoEnabledFlag()
    {
        List<OutputConfig> configs =
        [
            new ArtNetOutputConfig { Name = "Stage", Destination = "192.168.1.50", Net = 1, Universes = [1, 2] },
            new SacnOutputConfig { Priority = 150, UniverseOffset = 10 },
            new EnttecProOutputConfig { PortName = "COM4" },
            new OpenDmxOutputConfig { SerialNumber = "A1B2" },
            new VirtualOutputConfig(),
        ];

        var json = JsonSerializer.Serialize(configs);
        var restored = JsonSerializer.Deserialize<List<OutputConfig>>(json)!;

        Assert.DoesNotContain("enabled", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"type\":\"artnet\"", json);
        var artNet = Assert.IsType<ArtNetOutputConfig>(restored[0]);
        Assert.Equal("192.168.1.50", artNet.Destination);
        Assert.Equal([1, 2], artNet.Universes);
        Assert.Equal(1, artNet.Net);
        var sacn = Assert.IsType<SacnOutputConfig>(restored[1]);
        Assert.Equal(((SacnOutputConfig)configs[1]).Cid, sacn.Cid);
        Assert.Equal(150, sacn.Priority);
        Assert.Equal("COM4", Assert.IsType<EnttecProOutputConfig>(restored[2]).PortName);
        Assert.Equal("A1B2", Assert.IsType<OpenDmxOutputConfig>(restored[3]).SerialNumber);
        Assert.IsType<VirtualOutputConfig>(restored[4]);
    }

    private static void AssertError(OutputConfig config, string code)
    {
        Assert.Contains(config.Validate(), i => i.Code == code && i.Severity == ValidationSeverity.Error);
    }
}
