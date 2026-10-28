using System.Runtime.InteropServices;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// Compiles the project's functions into <see cref="FunctionProgram"/>s against a resolved patch. Functions that
/// reference others (chaser steps, fade scenes) are compiled on demand and shared; a reference back to a function that
/// is still being compiled (a loop) is skipped and logged. Runs off the engine thread (allocates freely).
/// </summary>
internal sealed class FunctionCompiler
{
    private readonly Project _project;
    private readonly PatchIndex _patch;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, LightFunction> _byId = [];
    private readonly Dictionary<Guid, FunctionProgram?> _compiled = [];
    private readonly HashSet<Guid> _compiling = [];
    private readonly Dictionary<Guid, EffectFixture?> _fixtures = [];
    private readonly ArrayInterner<int> _channelArrays = new();
    private readonly ArrayInterner<byte> _valueArrays = new();
    private readonly Dictionary<List<Guid>, EffectFixture[]> _fixtureLists = new(GuidListComparer.Instance);
    private readonly Dictionary<List<Guid>, EffectFixture[]> _colorFixtureLists = new(GuidListComparer.Instance);
    private Dictionary<Guid, Fixture>? _fixturesById;
    private int[] _scratchChannels = [];
    private byte[] _scratchLevels = [];

    private readonly LightFunction? _draft;

    /// <param name="project">Project whose functions (and fixtures/profiles) are compiled.</param>
    /// <param name="patch">Resolved patch.</param>
    /// <param name="logger">Logger for skipped references.</param>
    /// <param name="draft">
    /// Optional unsaved function that replaces the project's function with the same id (preview of an editor draft).
    /// </param>
    /// <param name="draftFunctions">
    /// Optional unsaved functions the draft references (e.g. a chaser draft's own step scenes): each replaces the
    /// project's function with the same id, or is added. The draft itself wins over an entry with its id.
    /// </param>
    public FunctionCompiler(
        Project project,
        PatchIndex patch,
        ILogger logger,
        LightFunction? draft = null,
        IReadOnlyList<LightFunction>? draftFunctions = null)
    {
        _project = project;
        _patch = patch;
        _logger = logger;
        _draft = draft;
        foreach (var function in project.Functions)
        {
            _byId.TryAdd(function.Id, function);
        }

        if (draftFunctions is not null)
        {
            foreach (var function in draftFunctions)
            {
                if (function is not null)
                {
                    _byId[function.Id] = function;
                }
            }
        }

        if (draft is not null)
        {
            _byId[draft.Id] = draft;
        }
    }

    /// <summary>Compiles only the draft passed to the constructor (preview). Null when it cannot be played.</summary>
    public FunctionProgram? CompileDraft()
    {
        if (_draft is null)
        {
            return null;
        }

        // Preview of a flash = the button is held while the editor is open, whatever its duration.
        if (_draft is FlashFunction flash)
        {
            return new FlashProgram(flash.Id, Fixtures(flash, colorOnly: false), flash.Color, 0, flash.UseWhiteChannel);
        }

        return Compile(_draft.Id);
    }

    /// <summary>Compiles every function; types the engine cannot play are left out.</summary>
    public Dictionary<Guid, FunctionProgram> CompileAll()
    {
        var result = new Dictionary<Guid, FunctionProgram>();
        foreach (var function in _project.Functions)
        {
            if (Compile(function.Id) is { } program)
            {
                result[function.Id] = program;
            }
        }

        return result;
    }

    private FunctionProgram? Compile(Guid functionId)
    {
        if (_compiled.TryGetValue(functionId, out var done))
        {
            return done;
        }

        if (!_byId.TryGetValue(functionId, out var function))
        {
            return null;
        }

        if (!_compiling.Add(functionId))
        {
            _logger.LogWarning("Function {Function} plays itself through its steps; the looping step is skipped", function.Name);
            return null;
        }

        var program = CompileCore(function);
        _compiling.Remove(functionId);
        _compiled[functionId] = program;
        return program;
    }

    private FunctionProgram? CompileCore(LightFunction function)
    {
        switch (function)
        {
            case SceneFunction scene:
                return CompileValues(scene.Id, scene.FadeInSeconds, scene.FadeOutSeconds, scene.Values);

            case ChaserFunction chaser:
                var chaserSteps = new List<StepProgram.Step>(chaser.Steps.Count);
                foreach (var step in chaser.Steps)
                {
                    chaserSteps.Add(new StepProgram.Step(Compile(step.FunctionId), step.FadeInSeconds, step.HoldSeconds, step.FadeOutSeconds));
                }

                return new StepProgram(chaser.Id, chaserSteps, chaser.RunOrder, chaser.Direction);

            case SequenceFunction sequence:
                var sequenceSteps = new List<StepProgram.Step>(sequence.Steps.Count);
                foreach (var step in sequence.Steps)
                {
                    var values = CompileValues(sequence.Id, 0, 0, step.Values);
                    sequenceSteps.Add(new StepProgram.Step(values, step.FadeInSeconds, step.HoldSeconds, step.FadeOutSeconds));
                }

                return new StepProgram(sequence.Id, sequenceSteps, sequence.RunOrder, sequence.Direction);

            case FadeFunction fade:
                return CompileFade(fade);

            case ColorCycleFunction cycle:
                // Spec §4: only fixtures with compatible colour channels take part.
                return new ColorCycleProgram(
                    cycle.Id,
                    Fixtures(cycle, colorOnly: true),
                    cycle.Colors,
                    cycle.HoldSeconds,
                    cycle.FadeSeconds,
                    cycle.FixtureOffsetSeconds,
                    cycle.UseWhiteChannel);

            case PulseFunction pulse:
                return new PulseProgram(
                    pulse.Id,
                    Fixtures(pulse, colorOnly: false),
                    pulse.Color,
                    pulse.Shape,
                    pulse.PeriodSeconds,
                    pulse.MinLevel,
                    pulse.MaxLevel,
                    pulse.UseWhiteChannel);

            case FlashFunction flash:
                return new FlashProgram(flash.Id, Fixtures(flash, colorOnly: false), flash.Color, flash.DurationSeconds, flash.UseWhiteChannel);

            case StrobeFunction strobe:
                return new StrobeProgram(
                    strobe.Id,
                    Fixtures(strobe, colorOnly: false),
                    strobe.Mode,
                    strobe.RateHz,
                    strobe.DutyCycle,
                    strobe.Color,
                    strobe.HardwareValue,
                    strobe.UseWhiteChannel);

            default:
                return null;
        }
    }

    /// <summary>
    /// Fixed values at resolved channels; values for missing fixtures/channels are skipped (validation reports them).
    /// Scene values come grouped per fixture, so the fixture lookup is reused while the fixture id repeats; equal
    /// channel/value arrays (show copies of the same scene) are shared.
    /// </summary>
    private SceneProgram CompileValues(Guid id, double fadeIn, double fadeOut, List<SceneValue> values)
    {
        if (_scratchChannels.Length < values.Count)
        {
            _scratchChannels = new int[values.Count];
            _scratchLevels = new byte[values.Count];
        }

        var channels = _scratchChannels;
        var levels = _scratchLevels;
        var count = 0;
        var lastFixtureId = Guid.Empty;
        PatchIndex.FixtureChannels? fixture = null;
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (i == 0 || value.FixtureId != lastFixtureId)
            {
                lastFixtureId = value.FixtureId;
                fixture = _patch.Find(lastFixtureId);
            }

            if (fixture is not null && fixture.TryGetFlat(value.ChannelKey, out var flat))
            {
                channels[count] = flat;
                levels[count] = value.Value;
                count++;
            }
        }

        return new SceneProgram(
            id,
            fadeIn,
            fadeOut,
            _channelArrays.Intern(channels.AsSpan(0, count)),
            _valueArrays.Intern(levels.AsSpan(0, count)));
    }

    private FadeProgram CompileFade(FadeFunction fade)
    {
        var order = new List<int>();
        var from = new Dictionary<int, short>();
        var to = new Dictionary<int, short>();
        Collect(fade.FromSceneId, from);
        Collect(fade.ToSceneId, to);

        var fromValues = new short[order.Count];
        var toValues = new short[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            fromValues[i] = from.TryGetValue(order[i], out var f) ? f : FadeProgram.Absent;
            toValues[i] = to.TryGetValue(order[i], out var t) ? t : FadeProgram.Absent;
        }

        return new FadeProgram(fade.Id, [.. order], fromValues, toValues, fade.DurationSeconds);

        void Collect(Guid? sceneId, Dictionary<int, short> target)
        {
            if (sceneId is not { } id)
            {
                return;
            }

            if (!_byId.TryGetValue(id, out var function) || function is not SceneFunction scene)
            {
                _logger.LogWarning("Fade {Fade} uses a scene that does not exist; that side is left empty", fade.Name);
                return;
            }

            foreach (var value in scene.Values)
            {
                if (_patch.TryGetFlat(value.FixtureId, value.ChannelKey, out var flat))
                {
                    if (!from.ContainsKey(flat) && !to.ContainsKey(flat))
                    {
                        order.Add(flat);
                    }

                    target[flat] = value.Value;
                }
            }
        }
    }

    /// <summary>
    /// The effect's fixtures that exist, are patched and have something to light (with colour when required). Show
    /// copies of one effect have equal fixture lists, so results are shared by list content (the project is not edited
    /// while compiling, so the lists can serve as keys).
    /// </summary>
    private EffectFixture[] Fixtures(FixtureEffectFunction effect, bool colorOnly)
    {
        var cache = colorOnly ? _colorFixtureLists : _fixtureLists;
        if (cache.TryGetValue(effect.FixtureIds, out var shared))
        {
            return shared;
        }

        var result = new List<EffectFixture>(effect.FixtureIds.Count);
        var seen = new HashSet<Guid>();
        foreach (var fixtureId in effect.FixtureIds)
        {
            if (seen.Add(fixtureId) && Resolve(fixtureId) is { } fixture && (colorOnly ? fixture.HasColor : !fixture.IsEmpty))
            {
                result.Add(fixture);
            }
        }

        EffectFixture[] fixtures = [.. result];
        cache[effect.FixtureIds] = fixtures;
        return fixtures;
    }

    /// <summary>Resolves a fixture's effect channels by channel type through its profile mode (cached per fixture).</summary>
    private EffectFixture? Resolve(Guid fixtureId)
    {
        if (_fixtures.TryGetValue(fixtureId, out var cached))
        {
            return cached;
        }

        EffectFixture? result = null;
        _fixturesById ??= BuildFixtureLookup(_project);
        var fixture = _fixturesById.GetValueOrDefault(fixtureId);
        var profile = fixture is null ? null : _project.FindProfile(fixture.ProfileId);
        var mode = profile?.FindMode(fixture!.ModeName);
        if (profile is not null && mode is not null)
        {
            List<int> dimmer = [], red = [], green = [], blue = [], white = [], amber = [], uv = [], strobe = [];
            List<short> strobeActive = [];
            foreach (var channel in profile.ResolveChannels(mode))
            {
                if (!_patch.TryGetFlat(fixtureId, channel.Key, out var flat))
                {
                    continue;
                }

                switch (channel.Type)
                {
                    case ChannelType.Dimmer: dimmer.Add(flat); break;
                    case ChannelType.Red: red.Add(flat); break;
                    case ChannelType.Green: green.Add(flat); break;
                    case ChannelType.Blue: blue.Add(flat); break;
                    case ChannelType.White: white.Add(flat); break;
                    case ChannelType.Amber: amber.Add(flat); break;
                    case ChannelType.UV: uv.Add(flat); break;
                    case ChannelType.Strobe:
                        strobe.Add(flat);
                        strobeActive.Add(FirstStrobingValue(channel));
                        break;
                }
            }

            result = new EffectFixture([.. dimmer], [.. red], [.. green], [.. blue], [.. white], [.. amber], [.. uv], [.. strobe], [.. strobeActive]);
        }

        _fixtures[fixtureId] = result;
        return result;
    }

    /// <summary>Fixtures by id; the first one wins on a duplicate id, like <see cref="Project.FindFixture"/>.</summary>
    private static Dictionary<Guid, Fixture> BuildFixtureLookup(Project project)
    {
        var result = new Dictionary<Guid, Fixture>(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            result.TryAdd(fixture.Id, fixture);
        }

        return result;
    }

    /// <summary>
    /// Start of the first range the shared <see cref="StrobeCapabilityClassifier"/> recognises as strobing, or -1 when the
    /// profile documents none: steady, closed and unrecognised ranges are skipped, and an undocumented strobe channel's
    /// values are never guessed (safety rule 7).
    /// </summary>
    private static short FirstStrobingValue(ChannelDefinition channel)
    {
        foreach (var capability in channel.Capabilities)
        {
            if (StrobeCapabilityClassifier.Classify(capability) == StrobeRangeKind.Strobing)
            {
                return (short)Math.Clamp(capability.Min, 0, 255);
            }
        }

        return -1;
    }

    /// <summary>Compares fixture id lists by content (order matters: it sets the effect's fixture order).</summary>
    private sealed class GuidListComparer : IEqualityComparer<List<Guid>>
    {
        public static readonly GuidListComparer Instance = new();

        public bool Equals(List<Guid>? x, List<Guid>? y) =>
            ReferenceEquals(x, y)
            || (x is not null && y is not null && CollectionsMarshal.AsSpan(x).SequenceEqual(CollectionsMarshal.AsSpan(y)));

        public int GetHashCode(List<Guid> obj)
        {
            var hash = default(HashCode);
            hash.AddBytes(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(obj)));
            return hash.ToHashCode();
        }
    }
}
