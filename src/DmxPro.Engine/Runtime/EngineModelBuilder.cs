using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// Resolves a project (fixtures + embedded profiles + functions + shows) into an <see cref="EngineModel"/>.
/// Runs on the caller's thread (never in the tick); the project must not be modified concurrently.
/// </summary>
internal static class EngineModelBuilder
{
    public static EngineModel Build(Project project, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(project);

        var universes = project.Universes.Where(DmxAddress.IsValidUniverse).ToArray();
        if (universes.Length == 0)
        {
            universes = [1];
        }

        var size = universes.Length * DmxAddress.UniverseSize;
        var defaults = new byte[size];
        var isIntensity = new bool[size];
        var isStrobe = new bool[size];
        var strobeSafe = new byte[size];
        var patch = new Dictionary<(Guid, string), PatchedChannel>();
        var index = new PatchIndex();

        foreach (var fixture in project.Fixtures)
        {
            if (!fixture.Enabled)
            {
                continue;
            }

            var profile = project.FindProfile(fixture.ProfileId);
            var mode = profile?.FindMode(fixture.ModeName);
            if (profile is null || mode is null)
            {
                logger.LogWarning(
                    "Fixture {Fixture} is not played: profile {Profile} / mode {Mode} not found",
                    fixture.Name,
                    fixture.ProfileId,
                    fixture.ModeName);
                continue;
            }

            var slot = Array.IndexOf(universes, fixture.Universe);
            if (slot < 0)
            {
                continue;
            }

            for (var offset = 0; offset < mode.Channels.Count; offset++)
            {
                var address = fixture.StartAddress + offset;
                if (!DmxAddress.IsValid(address) || profile.FindChannel(mode.Channels[offset]) is not { } channel)
                {
                    continue;
                }

                // Overlapping fixtures: the later fixture's channel meaning wins (the patch validator reports overlaps).
                var flat = (slot * DmxAddress.UniverseSize) + DmxAddress.ToIndex(address);
                defaults[flat] = channel.Default;
                isIntensity[flat] = channel.Type.IsIntensity();
                isStrobe[flat] = channel.Type == ChannelType.Strobe;
                strobeSafe[flat] = SafeValue(channel);

                patch[(fixture.Id, channel.Key)] = new PatchedChannel(fixture.Universe, address, channel.Type, channel.Default);
                index.Add(fixture.Id, channel.Key, flat);
            }
        }

        var functions = new FunctionCompiler(project, index, logger).CompileAll();

        var shows = new Dictionary<Guid, ShowProgram>();
        foreach (var show in project.Shows)
        {
            var items = new List<ShowItemProgram>();
            foreach (var item in show.AllItems.OrderBy(i => i.StartSeconds))
            {
                if (!item.HasValidTiming || !functions.TryGetValue(item.FunctionId, out var program))
                {
                    continue;
                }

                items.Add(new ShowItemProgram(program, item.StartSeconds, item.EndSeconds, item.FadeInSeconds, item.FadeOutSeconds));
            }

            var length = double.IsFinite(show.LengthSeconds) && show.LengthSeconds > 0 ? show.LengthSeconds : 0;
            shows[show.Id] = new ShowProgram(show.Id, length, [.. items]);
        }

        return new EngineModel(universes, defaults, isIntensity, isStrobe, strobeSafe, patch, functions, shows, index);
    }

    /// <summary>
    /// Compiles one (possibly unsaved) function against an already built model's patch, for the preview layer. Functions
    /// the draft references (chaser steps, fade scenes) are resolved from <paramref name="project"/> as saved; the draft
    /// replaces the project's function with the same id. A timed flash is compiled as held (the editor is open, so the
    /// "button" is down). Returns null for types the engine cannot play. Runs on the caller's thread; the project must
    /// not be modified concurrently. <paramref name="draftFunctions"/> (optional) are further unsaved functions the draft
    /// plays (e.g. a chaser draft's own step scenes): they replace (or add to) the project's functions with the same id.
    /// </summary>
    public static FunctionProgram? CompileDraft(
        Project project,
        LightFunction draft,
        EngineModel model,
        ILogger logger,
        IReadOnlyList<LightFunction>? draftFunctions = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        return new FunctionCompiler(project, model.PatchIndex, logger, draft, draftFunctions).CompileDraft();
    }

    /// <summary>
    /// Value used while strobe is not allowed (safety rule 8), using the shared <see cref="StrobeCapabilityClassifier"/>:
    /// the default when it lies in a steady range (safe / open / no function), otherwise the start of the first steady
    /// range. Without a steady range: the start of the first closed range when the default itself would strobe, otherwise
    /// the profile default (the value the profile says the fixture rests at; channels without capabilities rest there).
    /// </summary>
    private static byte SafeValue(ChannelDefinition channel)
    {
        ChannelCapability? firstSteady = null;
        ChannelCapability? firstClosed = null;
        var defaultFound = false;
        var defaultKind = StrobeRangeKind.Unknown;
        foreach (var capability in channel.Capabilities)
        {
            var kind = StrobeCapabilityClassifier.Classify(capability);
            if (!defaultFound && capability.Contains(channel.Default))
            {
                defaultFound = true;
                defaultKind = kind;
            }

            if (kind == StrobeRangeKind.Steady)
            {
                firstSteady ??= capability;
            }
            else if (kind == StrobeRangeKind.Closed)
            {
                firstClosed ??= capability;
            }
        }

        // The first range containing a value decides what it means (as in ChannelDefinition.FindCapability).
        if (defaultKind == StrobeRangeKind.Steady)
        {
            return channel.Default;
        }

        if (firstSteady is not null)
        {
            return ClampToByte(firstSteady.Min);
        }

        return defaultKind == StrobeRangeKind.Strobing && firstClosed is not null
            ? ClampToByte(firstClosed.Min)
            : channel.Default;
    }

    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
