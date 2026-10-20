using DmxPro.Core.Profiles;

namespace DmxPro.Core.Tests;

public class CopiedLightValuesTests
{
    private static ChannelDefinition Ch(string key, ChannelType type, string? coarseKey = null) =>
        new() { Key = key, Name = key, Type = type, CoarseKey = coarseKey };

    private static readonly ChannelDefinition[] Rgbw11 =
    [
        Ch("dimmer", ChannelType.Dimmer), Ch("red", ChannelType.Red), Ch("green", ChannelType.Green),
        Ch("blue", ChannelType.Blue), Ch("white", ChannelType.White), Ch("amber", ChannelType.Amber),
        Ch("strobe", ChannelType.Strobe), Ch("generic-8", ChannelType.Generic),
    ];

    private static CopiedLightValues CopyAll(string profileId, IReadOnlyList<ChannelDefinition> channels, params byte[] values) =>
        CopiedLightValues.Create(profileId, "Source", channels.Select((c, i) => (c, values[i])));

    [Fact]
    public void MatchTo_SameProfile_MatchesEveryChannelByKeyIncludingGeneric()
    {
        var copied = CopyAll("custom-a", Rgbw11, 255, 10, 20, 30, 40, 50, 60, 70);

        var matches = copied.MatchTo("CUSTOM-A", Rgbw11);

        Assert.Equal(Enumerable.Range(0, 8).Select(i => (i, (byte)new byte[] { 255, 10, 20, 30, 40, 50, 60, 70 }[i])), matches);
    }

    [Fact]
    public void MatchTo_OtherProfileWithDifferentOrder_MatchesByTypeNotPosition()
    {
        var copied = CopyAll("custom-a", Rgbw11, 255, 10, 20, 30, 40, 50, 60, 70);
        ChannelDefinition[] target = [Ch("b", ChannelType.Blue), Ch("r", ChannelType.Red), Ch("g", ChannelType.Green), Ch("d", ChannelType.Dimmer)];

        var matches = copied.MatchTo("generic-rgb", target);

        Assert.Equal([(0, (byte)30), (1, (byte)10), (2, (byte)20), (3, (byte)255)], matches);
    }

    [Fact]
    public void MatchTo_OtherProfile_GenericChannelsAreNotPasted()
    {
        var copied = CopyAll("custom-a", Rgbw11, 255, 10, 20, 30, 40, 50, 60, 70);
        ChannelDefinition[] target = [Ch("x", ChannelType.Generic), Ch("r", ChannelType.Red)];

        var matches = copied.MatchTo("other", target);

        Assert.Equal([(1, (byte)10)], matches);
    }

    [Fact]
    public void MatchTo_OtherProfile_SecondOccurrenceGoesToSecondOccurrence()
    {
        ChannelDefinition[] source = [Ch("w1", ChannelType.White), Ch("w2", ChannelType.White)];
        var copied = CopyAll("a", source, 11, 22);
        ChannelDefinition[] target = [Ch("x1", ChannelType.White), Ch("r", ChannelType.Red), Ch("x2", ChannelType.White), Ch("x3", ChannelType.White)];

        var matches = copied.MatchTo("b", target);

        Assert.Equal([(0, (byte)11), (2, (byte)22)], matches);
    }

    [Fact]
    public void MatchTo_PartialCopy_LeavesUncopiedChannelsUnmatched()
    {
        var copied = CopiedLightValues.Create("custom-a", "Source", [(Rgbw11[1], (byte)200)]);

        var matches = copied.MatchTo("custom-a", Rgbw11);

        Assert.Equal([(1, (byte)200)], matches);
    }

    [Fact]
    public void Create_IncludeFlags_CopiesOnlyIncludedChannelsInModeOrder()
    {
        var copied = CopiedLightValues.CreateFromMode(
            "a",
            "Source",
            Rgbw11.Select((c, i) => (c, (byte)(i * 10), i is 1 or 3)));

        Assert.Equal(["red", "blue"], copied.Channels.Select(c => c.Key));
        Assert.Equal([(byte)10, (byte)30], copied.Channels.Select(c => c.Value));
    }

    [Fact]
    public void Create_AllIncluded_SameAsCopyAllOverload()
    {
        var all = CopyAll("a", Rgbw11, 1, 2, 3, 4, 5, 6, 7, 8);
        var flagged = CopiedLightValues.CreateFromMode("a", "Source", Rgbw11.Select((c, i) => (c, (byte)(i + 1), true)));

        Assert.Equal(all.Channels, flagged.Channels);
    }

    [Fact]
    public void MatchTo_OtherProfilePartialCopyOfFirstOfTwoWhites_GoesToFirstWhite()
    {
        ChannelDefinition[] source = [Ch("w1", ChannelType.White), Ch("w2", ChannelType.White)];
        var copied = CopiedLightValues.CreateFromMode("a", "Source", source.Select((c, i) => (c, (byte)(50 + i), i == 0)));
        ChannelDefinition[] target = [Ch("x1", ChannelType.White), Ch("x2", ChannelType.White)];

        Assert.Equal([(0, (byte)50)], copied.MatchTo("b", target));
    }

    [Fact]
    public void MatchTo_OtherProfilePartialCopyOfSecondWhiteTargetHasOneWhite_MatchesNothing()
    {
        ChannelDefinition[] source = [Ch("w1", ChannelType.White), Ch("w2", ChannelType.White)];
        var copied = CopiedLightValues.CreateFromMode("a", "Source", source.Select((c, i) => (c, (byte)99, i == 1)));

        Assert.Empty(copied.MatchTo("b", [Ch("x1", ChannelType.White)]));
    }

    [Fact]
    public void MatchTo_OtherProfileFineBeforeItsCoarse_ResolvesCoarseByKey()
    {
        ChannelDefinition[] source = [Ch("pan-fine", ChannelType.Fine, "pan"), Ch("pan", ChannelType.Dimmer)];
        var copied = CopyAll("a", source, 7, 200);
        ChannelDefinition[] target = [Ch("p", ChannelType.Dimmer), Ch("p-fine", ChannelType.Fine, "p")];

        Assert.Equal([(0, (byte)200), (1, (byte)7)], copied.MatchTo("b", target));
    }

    [Fact]
    public void MatchTo_OtherProfileFineOfSecondRed_GoesToFineOfSecondRed()
    {
        ChannelDefinition[] source =
        [
            Ch("r1", ChannelType.Red), Ch("r1-fine", ChannelType.Fine, "r1"),
            Ch("r2", ChannelType.Red), Ch("r2-fine", ChannelType.Fine, "r2"),
        ];
        var copied = CopiedLightValues.CreateFromMode("a", "Source", source.Select((c, i) => (c, (byte)(i + 1), i == 3)));
        ChannelDefinition[] target =
        [
            Ch("x1", ChannelType.Red), Ch("x2", ChannelType.Red),
            Ch("x2-fine", ChannelType.Fine, "x2"), Ch("x1-fine", ChannelType.Fine, "x1"),
        ];

        Assert.Equal([(2, (byte)4)], copied.MatchTo("b", target));
    }

    [Fact]
    public void MatchTo_OtherProfileFineWithoutResolvableCoarse_IsSkipped()
    {
        ChannelDefinition[] source = [Ch("orphan-fine", ChannelType.Fine, "missing"), Ch("loose-fine", ChannelType.Fine)];
        var copied = CopyAll("a", source, 5, 6);
        ChannelDefinition[] target = [Ch("f1", ChannelType.Fine, "nope"), Ch("f2", ChannelType.Fine)];

        Assert.Empty(copied.MatchTo("b", target));
    }

    [Fact]
    public void MatchTo_OtherProfileFineOfGenericCoarse_IsSkipped()
    {
        ChannelDefinition[] source = [Ch("g", ChannelType.Generic), Ch("g-fine", ChannelType.Fine, "g")];
        var copied = CopyAll("a", source, 5, 6);
        ChannelDefinition[] target = [Ch("x", ChannelType.Generic), Ch("x-fine", ChannelType.Fine, "x")];

        Assert.Empty(copied.MatchTo("b", target));
    }

    [Fact]
    public void MatchTo_SameProfileFineWithoutCoarse_StillMatchesByKey()
    {
        ChannelDefinition[] source = [Ch("orphan-fine", ChannelType.Fine, "missing")];
        var copied = CopyAll("a", source, 9);

        Assert.Equal([(0, (byte)9)], copied.MatchTo("a", source));
    }

    [Fact]
    public void MatchTo_OtherProfileFineDoesNotCountAsCoarseOccurrence()
    {
        // A fine channel between two whites must not shift "the second White".
        ChannelDefinition[] source = [Ch("w1", ChannelType.White), Ch("w1-fine", ChannelType.Fine, "w1"), Ch("w2", ChannelType.White)];
        var copied = CopiedLightValues.CreateFromMode("a", "Source", source.Select((c, i) => (c, (byte)(i + 10), i == 2)));
        ChannelDefinition[] target = [Ch("x1", ChannelType.White), Ch("x2", ChannelType.White)];

        Assert.Equal([(1, (byte)12)], copied.MatchTo("b", target));
    }
}
