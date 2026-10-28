using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// Compiles the project's functions into <see cref="FunctionProgram"/>s against a resolved patch. Functions that
/// reference others (chaser steps) are compiled on demand and shared; a reference back to a function that
/// is still being compiled (a loop) is skipped and logged. Only scenes, chasers and legacy sequences compile; any other
/// (removed) function type compiles to nothing. Runs off the engine thread (allocates freely).
/// </summary>
internal sealed class FunctionCompiler
{
    private readonly Project _project;
    private readonly PatchIndex _patch;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, LightFunction> _byId = [];
    private readonly Dictionary<Guid, FunctionProgram?> _compiled = [];
    private readonly HashSet<Guid> _compiling = [];
    private readonly ArrayInterner<int> _channelArrays = new();
    private readonly ArrayInterner<byte> _valueArrays = new();
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

            default:
                // Colour cycle, fade, pulse, flash and strobe were removed (2026-10-28); project load converts them to
                // scenes and chasers, so one reaching the engine is a leftover: it plays nothing instead of failing.
                _logger.LogWarning("Function {Function} has a type the engine no longer plays; it is skipped", function.Name);
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
}
