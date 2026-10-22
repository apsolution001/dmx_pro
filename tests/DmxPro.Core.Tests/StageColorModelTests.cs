using DmxPro.Core.Profiles;
using DmxPro.Core.Stage;

namespace DmxPro.Core.Tests;

public class StageColorModelTests
{
    private const int Start = 10;

    [Fact]
    public void Build_RgbMode_ResolvesFootprintAndLightOutput()
    {
        var model = Model(Profile(Ch("r", ChannelType.Red), Ch("g", ChannelType.Green), Ch("b", ChannelType.Blue)));

        Assert.Equal(3, model.ChannelCount);
        Assert.True(model.HasLightOutput);
    }

    [Fact]
    public void Evaluate_RgbRedFull_IsRedFullIntensity()
    {
        var model = Model(Profile(Ch("r", ChannelType.Red), Ch("g", ChannelType.Green), Ch("b", ChannelType.Blue)));

        var state = model.Evaluate(Universe(255, 0, 0), Start, 0);

        Assert.Equal(new StageLightState(255, 0, 0, 1f, false), state);
    }

    [Fact]
    public void Evaluate_RgbHalfLevels_NormalisesHueAndKeepsIntensity()
    {
        var model = Model(Profile(Ch("r", ChannelType.Red), Ch("g", ChannelType.Green), Ch("b", ChannelType.Blue)));

        var state = model.Evaluate(Universe(128, 64, 0), Start, 0);

        Assert.Equal((byte)255, state.R);
        Assert.Equal((byte)128, state.G);
        Assert.Equal((byte)0, state.B);
        Assert.Equal(128 / 255f, state.Intensity, 4);
    }

    [Fact]
    public void Evaluate_CustomElevenChannelWithDimmerFirst_UsesProfileOffsets()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 0, 0, 255, 0, 0, 0, 77, 77, 77, 77), Start, 0);

        Assert.Equal(11, model.ChannelCount);
        Assert.Equal(new StageLightState(0, 0, 255, 1f, false), state);
    }

    [Fact]
    public void Evaluate_CustomElevenChannelPartialDimmer_ScalesIntensity()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(51, 255, 0, 0), Start, 0);

        Assert.Equal(new StageLightState(255, 0, 0, 0.2f, false), state);
    }

    [Fact]
    public void Evaluate_ShuffledChannelOrder_FindsChannelsByType()
    {
        // Blue, Dimmer, Generic, Red, Green: nothing at the "usual" positions.
        var model = Model(Profile(
            Ch("b", ChannelType.Blue),
            Ch("dim", ChannelType.Dimmer),
            Ch("x", ChannelType.Generic),
            Ch("r", ChannelType.Red),
            Ch("g", ChannelType.Green)));

        var state = model.Evaluate(Universe(0, 255, 255, 0, 255), Start, 0);

        Assert.Equal(new StageLightState(0, 255, 0, 1f, false), state);
    }

    [Fact]
    public void Evaluate_DimmerOnly_IsWarmWhiteScaledByDimmer()
    {
        var model = Model(Profile(Ch("dim", ChannelType.Dimmer)));

        var state = model.Evaluate(Universe(51), Start, 0);

        Assert.True(model.HasLightOutput);
        Assert.Equal(new StageLightState(255, 240, 210, 0.2f, false), state);
    }

    [Fact]
    public void Build_UnknownChannelsOnly_HasNoLightOutput()
    {
        var model = Model(Profile(Ch("a", ChannelType.Generic), Ch("s", ChannelType.Speed), Ch("m", ChannelType.Mode)));

        Assert.False(model.HasLightOutput);
        Assert.Equal(StageLightState.Off, model.Evaluate(Universe(255, 255, 255), Start, 0));
    }

    [Fact]
    public void Build_ModeKeyMissingFromProfile_KeepsOffsetsOfLaterChannels()
    {
        var profile = Profile(Ch("r", ChannelType.Red), Ch("g", ChannelType.Green));
        profile.Modes[0].Channels.Insert(0, "missing");
        var model = StageColorModel.Build(profile, profile.Modes[0]);

        var state = model.Evaluate(Universe(255, 0, 255), Start, 0);

        Assert.Equal(3, model.ChannelCount);
        Assert.Equal(new StageLightState(0, 255, 0, 1f, false), state);
    }

    [Fact]
    public void Evaluate_FineChannel_IsIgnored()
    {
        var model = Model(Profile(
            Ch("dim", ChannelType.Dimmer),
            new ChannelDefinition { Key = "dimfine", Type = ChannelType.Fine, CoarseKey = "dim" },
            Ch("r", ChannelType.Red)));

        var state = model.Evaluate(Universe(0, 255, 255), Start, 0);

        Assert.Equal(0f, state.Intensity);
    }

    [Fact]
    public void Evaluate_FootprintPastAddress512_ReadsMissingChannelsAsZero()
    {
        var model = Model(Profile(Ch("r", ChannelType.Red), Ch("g", ChannelType.Green), Ch("b", ChannelType.Blue)));
        var universe = new byte[512];
        universe[510] = 255; // address 511 = red; green = 512, blue would be address 513

        var state = model.Evaluate(universe, 511, 0);

        Assert.Equal(new StageLightState(255, 0, 0, 1f, false), state);
    }

    [Fact]
    public void Evaluate_SpanLongerThan512_DoesNotReadPastAddress512()
    {
        var model = Model(Profile(Ch("r", ChannelType.Red), Ch("g", ChannelType.Green)));
        var universe = new byte[600];
        universe[512] = 255; // would be "address 513"

        var state = model.Evaluate(universe, 512, 0);

        Assert.Equal(StageLightState.Off, state);
    }

    [Fact]
    public void Evaluate_EmptySpan_IsDark()
    {
        var model = Model(OwnerCustom());

        Assert.Equal(StageLightState.Off, model.Evaluate([], Start, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(1000)]
    public void Evaluate_InvalidStartAddress_IsDark(int startAddress)
    {
        var model = Model(Profile(Ch("r", ChannelType.Red)));
        var universe = new byte[512];
        Array.Fill(universe, (byte)255);

        Assert.Equal(StageLightState.Off, model.Evaluate(universe, startAddress, 0));
    }

    [Fact]
    public void Evaluate_WhiteOnly_IsWhite()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 0, 0, 0, 255), Start, 0);

        Assert.Equal(new StageLightState(255, 255, 255, 1f, false), state);
    }

    [Fact]
    public void Evaluate_AmberOnly_IsAmberHue()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 0, 0, 0, 0, 255), Start, 0);

        Assert.Equal(new StageLightState(255, 191, 0, 1f, false), state);
    }

    [Fact]
    public void Evaluate_RedPlusAmber_IsWarmerThanAmberAndClampsIntensity()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 255, 0, 0, 0, 255), Start, 0);

        // Red 2.0 + green 0.75 => hue (255, 96, 0), intensity clamped to 1.
        Assert.Equal(new StageLightState(255, 96, 0, 1f, false), state);
    }

    [Fact]
    public void Evaluate_UvOnly_IsVioletAndWeaker()
    {
        var model = Model(Profile(Ch("r", ChannelType.Red), Ch("uv", ChannelType.UV)));

        var state = model.Evaluate(Universe(0, 255), Start, 0);

        Assert.Equal((byte)128, state.R);
        Assert.Equal((byte)0, state.G);
        Assert.Equal((byte)255, state.B);
        Assert.Equal(0.8f, state.Intensity, 4);
    }

    [Fact]
    public void Evaluate_DimmerZero_IsDark()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(0, 255, 255, 255, 255), Start, 0);

        Assert.Equal(0f, state.Intensity);
    }

    [Fact]
    public void Evaluate_DimmerFullAllColoursZero_IsDark()
    {
        var model = Model(OwnerCustom());

        Assert.Equal(StageLightState.Off, model.Evaluate(Universe(255), Start, 0));
    }

    [Fact]
    public void Evaluate_StrobeZeroWithoutCapabilities_IsSteady()
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 255, 0, 0, 0, 0, 0), Start, 0.5);

        Assert.False(state.IsStrobing);
        Assert.Equal(1f, state.Intensity);
    }

    [Theory]
    [InlineData(0.0, 1f)] // phase 0 => on
    [InlineData(0.1, 1f)] // 1 Hz: on for the first third
    [InlineData(0.5, 0f)] // off
    [InlineData(1.2, 1f)] // next period, on again
    [InlineData(1.9, 0f)]
    public void Evaluate_StrobeLowestValueWithoutCapabilities_FlashesAtOneHertz(double time, float expected)
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 255, 0, 0, 0, 0, 1), Start, time);

        Assert.True(state.IsStrobing);
        Assert.Equal(expected, state.Intensity);
        Assert.Equal((byte)255, state.R);
    }

    [Theory]
    [InlineData(0.01, 1f)] // 20 Hz: period 0.05 s, on for the first 1/60 s
    [InlineData(0.03, 0f)]
    [InlineData(0.051, 1f)]
    public void Evaluate_StrobeFullWithoutCapabilities_FlashesAtTwentyHertz(double time, float expected)
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 255, 0, 0, 0, 0, 255), Start, time);

        Assert.True(state.IsStrobing);
        Assert.Equal(expected, state.Intensity);
    }

    [Fact]
    public void Evaluate_StrobeAtOrBelowDefaultWithoutCapabilities_IsSteady()
    {
        var profile = OwnerCustom();
        profile.FindChannel("strobe")!.Default = 5;
        var model = Model(profile);

        Assert.False(model.Evaluate(Universe(255, 255, 0, 0, 0, 0, 5), Start, 0.5).IsStrobing);
        Assert.True(model.Evaluate(Universe(255, 255, 0, 0, 0, 0, 6), Start, 0.5).IsStrobing);
    }

    [Fact]
    public void Evaluate_StrobeInSafeCapability_IsSteady()
    {
        var model = Model(StrobeProfile(
            new ChannelCapability { Min = 0, Max = 9, Label = "No strobe", Safe = true },
            new ChannelCapability { Min = 10, Max = 255, Label = "Strobe slow to fast" }));

        var state = model.Evaluate(Universe(255, 9), Start, 0.5);

        Assert.False(state.IsStrobing);
        Assert.Equal(1f, state.Intensity);
    }

    [Theory]
    [InlineData(10, 0.2, 1f)] // range start = 1 Hz: on until 1/3 s
    [InlineData(10, 0.5, 0f)]
    [InlineData(255, 0.01, 1f)] // range end = 20 Hz
    [InlineData(255, 0.03, 0f)]
    public void Evaluate_StrobeInStrobingCapability_FlashesOverThatRange(byte strobe, double time, float expected)
    {
        var model = Model(StrobeProfile(
            new ChannelCapability { Min = 0, Max = 9, Label = "No strobe", Safe = true },
            new ChannelCapability { Min = 10, Max = 255, Label = "Strobe slow to fast" }));

        var state = model.Evaluate(Universe(255, strobe), Start, time);

        Assert.True(state.IsStrobing);
        Assert.Equal(expected, state.Intensity);
    }

    [Fact]
    public void Evaluate_StrobeInClosedCapability_IsDarkNotStrobing()
    {
        var model = Model(StrobeProfile(
            new ChannelCapability { Min = 0, Max = 4, Label = "Blackout" },
            new ChannelCapability { Min = 5, Max = 9, Label = "Open", Safe = true },
            new ChannelCapability { Min = 10, Max = 255, Label = "Strobe" }));

        var closed = model.Evaluate(Universe(255, 2), Start, 0);
        var open = model.Evaluate(Universe(255, 7), Start, 0);

        Assert.Equal(new StageLightState(255, 0, 0, 0f, false), closed);
        Assert.Equal(new StageLightState(255, 0, 0, 1f, false), open);
    }

    [Fact]
    public void Evaluate_StrobeOpenLabelWithoutSafeFlag_IsSteady()
    {
        var model = Model(StrobeProfile(
            new ChannelCapability { Min = 0, Max = 15, Label = "Shutter open" },
            new ChannelCapability { Min = 16, Max = 255, Label = "Strobe" }));

        Assert.False(model.Evaluate(Universe(255, 12), Start, 0.5).IsStrobing);
        Assert.True(model.Evaluate(Universe(255, 16), Start, 0.5).IsStrobing);
    }

    [Fact]
    public void Evaluate_StrobeValueNoCapabilityCovers_FallsBackToDefault()
    {
        var model = Model(StrobeProfile(new ChannelCapability { Min = 10, Max = 255, Label = "Strobe" }));

        Assert.False(model.Evaluate(Universe(255, 0), Start, 0.5).IsStrobing);
        Assert.True(model.Evaluate(Universe(255, 5), Start, 0.5).IsStrobing);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.9)]
    public void Evaluate_StrobeOddTimes_StaysInRange(double time)
    {
        var model = Model(OwnerCustom());

        var state = model.Evaluate(Universe(255, 255, 0, 0, 0, 0, 128), Start, time);

        Assert.True(state.IsStrobing);
        Assert.InRange(state.Intensity, 0f, 1f);
    }

    [Fact]
    public void Evaluate_ManyCalls_AllocatesNothing()
    {
        var model = Model(OwnerCustom());
        var universe = Universe(200, 255, 128, 64, 32, 16, 100);
        model.Evaluate(universe, Start, 0); // warm up / JIT

        // Tiered JIT can promote the method mid-loop and that bookkeeping is counted on this thread, so one round can
        // show a few bytes that are not ours. Real per-call allocations show up in every round; take the best round.
        var sum = 0f;
        var allocated = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++)
            {
                sum += model.Evaluate(universe, Start, i * 0.001).Intensity;
            }

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, allocated);
        Assert.True(sum >= 0);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var profile = Profile(Ch("r", ChannelType.Red));

        Assert.Throws<ArgumentNullException>(() => StageColorModel.Build(null!, profile.Modes[0]));
        Assert.Throws<ArgumentNullException>(() => StageColorModel.Build(profile, null!));
    }

    /// <summary>A 512-value universe with <paramref name="values"/> starting at <see cref="Start"/>.</summary>
    private static byte[] Universe(params byte[] values)
    {
        var universe = new byte[512];
        values.CopyTo(universe, Start - 1);
        return universe;
    }

    private static StageColorModel Model(FixtureProfile profile) => StageColorModel.Build(profile, profile.Modes[0]);

    private static ChannelDefinition Ch(string key, ChannelType type) => new() { Key = key, Name = key, Type = type };

    /// <summary>One mode with all channels in the given order.</summary>
    private static FixtureProfile Profile(params ChannelDefinition[] channels)
    {
        var profile = new FixtureProfile { Id = "stage-test", Manufacturer = "Test", Model = "Stage" };
        var mode = new FixtureMode { Name = "mode" };
        foreach (var channel in channels)
        {
            profile.Channels.Add(channel);
            mode.Channels.Add(channel.Key);
        }

        profile.Modes.Add(mode);
        return profile;
    }

    /// <summary>The owner's custom 11ch: Dimmer, Red, Green, Blue, White, Amber, Strobe, 4x Generic.</summary>
    private static FixtureProfile OwnerCustom()
    {
        var dimmer = Ch("dim", ChannelType.Dimmer);
        dimmer.Default = 255;
        return Profile(
            dimmer,
            Ch("r", ChannelType.Red),
            Ch("g", ChannelType.Green),
            Ch("b", ChannelType.Blue),
            Ch("w", ChannelType.White),
            Ch("a", ChannelType.Amber),
            Ch("strobe", ChannelType.Strobe),
            Ch("x1", ChannelType.Generic),
            Ch("x2", ChannelType.Generic),
            Ch("x3", ChannelType.Generic),
            Ch("x4", ChannelType.Generic));
    }

    /// <summary>Red + strobe with the given capabilities.</summary>
    private static FixtureProfile StrobeProfile(params ChannelCapability[] capabilities)
    {
        var strobe = Ch("strobe", ChannelType.Strobe);
        strobe.Capabilities = [.. capabilities];
        return Profile(Ch("r", ChannelType.Red), strobe);
    }
}
