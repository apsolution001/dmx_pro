using DmxPro.Core.Functions;
using DmxPro.Core.Profiles;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Projects;

/// <summary>
/// Load-time conversion of the retired function types (owner decision 2026-10-28: DMX Pro offers only Scene and
/// Chaser) into scenes and chasers. Each <see cref="ColorCycleFunction"/>, <see cref="FadeFunction"/>,
/// <see cref="PulseFunction"/>, <see cref="FlashFunction"/> and <see cref="StrobeFunction"/> is replaced in place by a
/// function with the same id, name and ownership, so Virtual Console widgets, show items, chaser steps and other
/// references keep working. Chaser steps play new hidden own-look scenes owned by the chaser (inserted right after it).
/// </summary>
/// <remarks>
/// The looks are built like the engine played the old effects: lights resolved through their profile mode by channel
/// type (never by offset), the shared effect colour policy (<see cref="FixtureEffectFunction"/>), lights that are
/// missing, unresolvable or have nothing to light are left out. Values are rounded half away from zero and clamped.
/// </remarks>
public static class LegacyFunctionMigration
{
    /// <summary>
    /// Fastest on/off rate a converted software strobe chaser uses: the app's default maximum strobe rate. A chaser step
    /// is at least <see cref="FunctionEditing.MinStepSeconds"/> long, so a 50 % on/off chaser cannot go faster anyway.
    /// </summary>
    public const double MaxConvertedStrobeRateHz = 10;

    /// <summary>True for a function of a retired type that <see cref="Migrate"/> converts.</summary>
    public static bool IsLegacy(LightFunction function) =>
        function is ColorCycleFunction or FadeFunction or PulseFunction or FlashFunction or StrobeFunction;

    /// <summary>
    /// Replaces every retired function in <paramref name="project"/> (same list position) by its Scene or Chaser and
    /// inserts the chaser's own step scenes right after it. Function widgets that started a Flash function become
    /// momentary (<see cref="FunctionWidgetMode.Flash"/>) buttons, so the converted scene still releases on button-up
    /// (safety rule 5). Running it again changes nothing.
    /// </summary>
    /// <returns>The number of functions converted.</returns>
    public static int Migrate(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var legacyIds = new HashSet<Guid>();
        var flashIds = new HashSet<Guid>();
        foreach (var function in project.Functions)
        {
            if (function is not null && IsLegacy(function))
            {
                legacyIds.Add(function.Id);
                if (function is FlashFunction)
                {
                    flashIds.Add(function.Id);
                }
            }
        }

        if (legacyIds.Count == 0)
        {
            return 0;
        }

        // Fades only ever played real scenes: resolve their scenes against the project as it was loaded, so a fade that
        // pointed at a flash (now a scene) still treats that side as empty, whatever the list order.
        var originalScenes = new Dictionary<Guid, SceneFunction>();
        foreach (var function in project.Functions)
        {
            if (function is SceneFunction scene)
            {
                originalScenes.TryAdd(scene.Id, scene);
            }
        }

        var converted = 0;
        for (var i = 0; i < project.Functions.Count; i++)
        {
            var function = project.Functions[i];
            if (function is null || !IsLegacy(function))
            {
                continue;
            }

            var (replacement, scenes) = Convert(project, function, originalScenes);
            project.Functions[i] = replacement;
            project.Functions.InsertRange(i + 1, scenes);
            i += scenes.Count;
            converted++;
        }

        MakeFlashButtonsMomentary(project, flashIds);
        return converted;
    }

    private static (LightFunction Replacement, List<LightFunction> Scenes) Convert(
        Project project,
        LightFunction legacy,
        Dictionary<Guid, SceneFunction> originalScenes) => legacy switch
        {
            ColorCycleFunction cycle => ConvertColorCycle(project, cycle),
            FadeFunction fade => ConvertFade(fade, originalScenes),
            PulseFunction pulse => ConvertPulse(project, pulse),
            FlashFunction flash => ConvertFlash(project, flash),
            StrobeFunction strobe => ConvertStrobe(project, strobe),
            _ => throw new ArgumentOutOfRangeException(nameof(legacy), legacy.Type, "Not a retired function type."),
        };

    // ───────────────────────────── Colour cycle ─────────────────────────────

    /// <summary>
    /// Looping chaser. Without a per-light offset: one step per colour (fade in = fade out = the cycle's fade, hold = its
    /// hold), so the steps crossfade like the colours did. With an offset (a colour chase across the lights): one step per
    /// moment any light starts or ends a fade, each holding everybody's colour at that moment, fading from the previous
    /// step when any light changes in between and cutting otherwise.
    /// </summary>
    private static (LightFunction, List<LightFunction>) ConvertColorCycle(Project project, ColorCycleFunction cycle)
    {
        var chaser = NewChaser(cycle, RunOrder.Loop);
        var scenes = new List<LightFunction>();
        var lights = ResolveLights(project, cycle.FixtureIds, colorOnly: true);
        var colors = cycle.Colors ?? [];
        if (lights.Count == 0 || colors.Count == 0)
        {
            return (chaser, scenes);
        }

        // Same timing repair as the old program: a slot is at least one chaser step long; the fade wins over the hold.
        var fade = Seconds(cycle.FadeSeconds);
        var slot = Math.Max(Seconds(cycle.HoldSeconds) + fade, FunctionEditing.MinStepSeconds);
        var hold = slot - fade;
        var offset = double.IsFinite(cycle.FixtureOffsetSeconds)
            ? Math.Clamp(cycle.FixtureOffsetSeconds, -FunctionEditing.MaxSeconds, FunctionEditing.MaxSeconds)
            : 0;

        if (colors.Count == 1)
        {
            var only = OwnScene(chaser, "Color 1");
            foreach (var light in lights)
            {
                WriteColor(only, light, colors[0].R, colors[0].G, colors[0].B, 1, cycle.UseWhiteChannel);
            }

            scenes.Add(only);
            chaser.AddStep(only.Id, slot, 0, 0);
            return (chaser, scenes);
        }

        if (offset == 0 || lights.Count == 1)
        {
            for (var c = 0; c < colors.Count; c++)
            {
                var scene = OwnScene(chaser, $"Color {c + 1}");
                foreach (var light in lights)
                {
                    WriteColor(scene, light, colors[c].R, colors[c].G, colors[c].B, 1, cycle.UseWhiteChannel);
                }

                scenes.Add(scene);
                chaser.AddStep(scene.Id, hold, fade, fade);
            }

            return (chaser, scenes);
        }

        AddOffsetCycleSteps(chaser, scenes, lights, colors, hold, fade, slot, offset, cycle.UseWhiteChannel);
        return (chaser, scenes);
    }

    private static void AddOffsetCycleSteps(
        ChaserFunction chaser,
        List<LightFunction> scenes,
        List<Light> lights,
        List<RgbColor> colors,
        double hold,
        double fade,
        double slot,
        double offset,
        bool useWhite)
    {
        var cycleLength = colors.Count * slot;
        var times = new List<double>();
        for (var f = 0; f < lights.Count; f++)
        {
            for (var c = 0; c < colors.Count; c++)
            {
                times.Add(Wrap((c * slot) + (f * offset), cycleLength));
                if (fade > 0)
                {
                    times.Add(Wrap((c * slot) + hold + (f * offset), cycleLength));
                }
            }
        }

        times.Sort();

        // Moments closer than one chaser step are merged (the engine would stretch such steps and drift).
        var points = new List<double>();
        foreach (var time in times)
        {
            if (points.Count == 0 || time - points[^1] >= FunctionEditing.MinStepSeconds)
            {
                points.Add(time);
            }
        }

        if (points.Count > 1 && points[0] + cycleLength - points[^1] < FunctionEditing.MinStepSeconds)
        {
            points.RemoveAt(points.Count - 1);
        }

        var looks = new List<SceneFunction>(points.Count);
        for (var p = 0; p < points.Count; p++)
        {
            var scene = OwnScene(chaser, $"Step {p + 1}");
            for (var f = 0; f < lights.Count; f++)
            {
                var (r, g, b) = CycleColorAt(colors, points[p] - (f * offset), hold, fade, slot, cycleLength);
                WriteColor(scene, lights[f], r, g, b, 1, useWhite);
            }

            looks.Add(scene);
        }

        var count = points.Count;
        if (fade <= 0)
        {
            // Without fades every light holds between its changes: step p shows the look at points[p] until the next
            // moment, with hard cuts, exactly like the old program (also at the start).
            for (var p = 0; p < count; p++)
            {
                var length = count == 1 ? cycleLength : Wrap(points[(p + 1) % count] - points[p], cycleLength);
                scenes.Add(looks[p]);
                chaser.AddStep(looks[p].Id, length, 0, 0);
            }

            return;
        }

        // With fades the colours change continuously: step p plays the time from the previous moment up to points[p]
        // and fades in to the look at points[p] when a light changes in between (the previous step fades out over the
        // same time), else it holds.
        var fadeIns = new double[count];
        var lengths = new double[count];
        for (var p = 0; p < count; p++)
        {
            var previous = (p + count - 1) % count;
            var gap = count == 1 ? cycleLength : Wrap(points[p] - points[previous], cycleLength);
            lengths[p] = gap;
            fadeIns[p] = SameValues(looks[previous], looks[p]) ? 0 : gap;
        }

        for (var p = 0; p < count; p++)
        {
            scenes.Add(looks[p]);
            chaser.AddStep(looks[p].Id, lengths[p] - fadeIns[p], fadeIns[p], fadeIns[(p + 1) % count]);
        }
    }

    /// <summary>The old program's colour of a light at cycle time <paramref name="time"/> (hold, then linear crossfade).</summary>
    private static (double R, double G, double B) CycleColorAt(List<RgbColor> colors, double time, double hold, double fade, double slot, double cycleLength)
    {
        var count = colors.Count;
        var position = Wrap(time, cycleLength);
        var index = Math.Min((int)(position / slot), count - 1);
        var within = position - (index * slot);
        double r = colors[index].R, g = colors[index].G, b = colors[index].B;
        if (fade > 0 && within > hold)
        {
            var next = colors[(index + 1) % count];
            var p = Math.Min(1, (within - hold) / fade);
            r += (next.R - r) * p;
            g += (next.G - g) * p;
            b += (next.B - b) * p;
        }

        return (r, g, b);
    }

    // ───────────────────────────── Fade ─────────────────────────────

    /// <summary>
    /// Single-shot chaser: an own-look copy of From (cut in, crossfading out over the duration), then an own-look copy of
    /// To that fades in over the duration and holds for the longest step time. Without From (current look, missing or
    /// not a scene) only the To step; without To (or not a scene) an empty chaser, like the fade that output nothing.
    /// Scenes the fade owned itself are reused as the steps instead of copied.
    /// </summary>
    private static (LightFunction, List<LightFunction>) ConvertFade(FadeFunction fade, Dictionary<Guid, SceneFunction> originalScenes)
    {
        var chaser = NewChaser(fade, RunOrder.SingleShot);
        var scenes = new List<LightFunction>();
        var to = FindScene(fade.ToSceneId, originalScenes);
        if (to is null)
        {
            return (chaser, scenes);
        }

        var duration = Seconds(fade.DurationSeconds);
        var from = duration > 0 ? FindScene(fade.FromSceneId, originalScenes) : null;
        if (from is not null)
        {
            var fromStep = StepScene(chaser, from, scenes);
            chaser.AddStep(fromStep.Id, 0, 0, duration);
        }

        var toStep = StepScene(chaser, to, scenes);
        chaser.AddStep(toStep.Id, FunctionEditing.MaxSeconds, duration, 0);
        return (chaser, scenes);
    }

    private static SceneFunction? FindScene(Guid? id, Dictionary<Guid, SceneFunction> originalScenes) =>
        id is { } sceneId && originalScenes.TryGetValue(sceneId, out var scene) ? scene : null;

    /// <summary>The scene a fade step plays: the fade's own scene as-is, else a new own-look copy of its values.</summary>
    private static SceneFunction StepScene(ChaserFunction chaser, SceneFunction source, List<LightFunction> scenes)
    {
        if (source.OwnerFunctionId == chaser.Id)
        {
            return source;
        }

        foreach (var added in scenes)
        {
            if (added is SceneFunction copy && copy.CopiedFromId == source.Id)
            {
                return copy;
            }
        }

        var scene = ChaserSteps.NewOwnLookScene(
            chaser.Id,
            string.IsNullOrWhiteSpace(source.Name) ? $"Step {scenes.Count + 1}" : source.Name,
            source.Values ?? []);
        scene.CopiedFromId = source.Id;
        scenes.Add(scene);
        return scene;
    }

    // ───────────────────────────── Pulse ─────────────────────────────

    /// <summary>
    /// Looping chaser "Dark" (minimum level) ↔ "Bright" (maximum level), each fading in over half the period while the
    /// other fades out, starting dark like the pulse did. With a colour the lights show it (level on the Dimmer when the
    /// mode has one, else scaled colour); without a colour only Dimmer channels pulse and lights without one are left
    /// alone. Equal levels give one steady step.
    /// </summary>
    private static (LightFunction, List<LightFunction>) ConvertPulse(Project project, PulseFunction pulse)
    {
        var chaser = NewChaser(pulse, RunOrder.Loop);
        var scenes = new List<LightFunction>();
        var lights = ResolveLights(project, pulse.FixtureIds, colorOnly: false);
        var period = double.IsFinite(pulse.PeriodSeconds)
            ? Math.Clamp(pulse.PeriodSeconds, PulseFunction.MinPeriodSeconds, FunctionEditing.MaxSeconds)
            : PulseFunction.DefaultPeriodSeconds;
        var min = Level(pulse.MinLevel, 0);
        var max = Level(pulse.MaxLevel, 1);

        var dark = PulseLook(chaser, "Dark", lights, pulse.Color, min, pulse.UseWhiteChannel);
        if (dark.Values.Count == 0)
        {
            return (chaser, scenes);
        }

        if (min.Equals(max))
        {
            dark.Name = "Steady";
            scenes.Add(dark);
            chaser.AddStep(dark.Id, period, 0, 0);
            return (chaser, scenes);
        }

        var bright = PulseLook(chaser, "Bright", lights, pulse.Color, max, pulse.UseWhiteChannel);
        var half = period / 2;
        scenes.Add(dark);
        scenes.Add(bright);
        chaser.AddStep(dark.Id, 0, half, half);
        chaser.AddStep(bright.Id, 0, half, half);
        return (chaser, scenes);
    }

    private static SceneFunction PulseLook(ChaserFunction chaser, string name, List<Light> lights, RgbColor? color, double level, bool useWhite)
    {
        var scene = OwnScene(chaser, name);
        foreach (var light in lights)
        {
            if (color is { } c)
            {
                WriteColor(scene, light, c.R, c.G, c.B, level, useWhite);
            }
            else
            {
                Set(scene, light.Id, light.Dimmer, 255 * level);
            }
        }

        return scene;
    }

    // ───────────────────────────── Flash ─────────────────────────────

    /// <summary>
    /// A Scene with the flash colour at full level and no fades (instant on, instant release). A flash with an
    /// auto-release time becomes a single-shot chaser holding that look for the time instead, so it still ends by itself.
    /// </summary>
    private static (LightFunction, List<LightFunction>) ConvertFlash(Project project, FlashFunction flash)
    {
        var lights = ResolveLights(project, flash.FixtureIds, colorOnly: false);
        var duration = Seconds(flash.DurationSeconds);
        if (duration <= 0)
        {
            var scene = new SceneFunction();
            CopyIdentity(flash, scene);
            WriteLights(scene, lights, flash.Color, flash.UseWhiteChannel);
            return (scene, []);
        }

        var chaser = NewChaser(flash, RunOrder.SingleShot);
        var look = OwnScene(chaser, "Flash");
        WriteLights(look, lights, flash.Color, flash.UseWhiteChannel);
        chaser.AddStep(look.Id, duration, 0, 0);
        return (chaser, [look]);
    }

    private static void MakeFlashButtonsMomentary(Project project, HashSet<Guid> flashIds)
    {
        if (flashIds.Count == 0 || project.ConsolePages is null)
        {
            return;
        }

        foreach (var page in project.ConsolePages)
        {
            if (page?.Widgets is null)
            {
                continue;
            }

            foreach (var widget in page.Widgets)
            {
                if (widget is FunctionWidget button && flashIds.Contains(button.FunctionId))
                {
                    button.Mode = FunctionWidgetMode.Flash;
                }
            }
        }
    }

    // ───────────────────────────── Strobe ─────────────────────────────

    /// <summary>
    /// Only a <b>Hardware</b>-mode strobe (set explicitly by the user) uses the lights' profile Strobe channels: the set
    /// value, else the start of the first documented strobing range, with the colour steadily on. When every light can do
    /// that the result is a Scene. A <b>Software</b>-mode strobe never drives a Strobe channel to a strobing value
    /// (safety rule 8: strobe-channel values are not rate-limited by the engine and their Hz meaning is unknown): every
    /// light, also one with a strobe channel, flashes its colour in a looping 2-step "On"/"Off" chaser (hard cuts) at the
    /// strobe's rate, capped at <see cref="MaxConvertedStrobeRateHz"/> and at least one chaser step per half, with the duty
    /// cycle, so the engine's chaser speed limit governs it. Strobe channels not used for a hardware strobe are held at
    /// the profile's documented open / no-strobe value in every step (see <see cref="NonStrobingValue"/>) so the colour
    /// shows. Lights a hardware strobe cannot drive fall back to the chaser. Without a valid rate the lights stay steadily on.
    /// </summary>
    private static (LightFunction, List<LightFunction>) ConvertStrobe(Project project, StrobeFunction strobe)
    {
        var lights = ResolveLights(project, strobe.FixtureIds, colorOnly: false);
        var rate = double.IsFinite(strobe.RateHz) && strobe.RateHz > 0 ? Math.Min(strobe.RateHz, MaxConvertedStrobeRateHz) : 0;
        var hardware = strobe.Mode == StrobeMode.Hardware;
        var color = strobe.Color;

        var on = new SceneFunction();
        var off = new SceneFunction();
        var needsSoftware = false;
        foreach (var light in lights)
        {
            var usedChannel = false;
            if (hardware)
            {
                foreach (var channel in light.Strobe)
                {
                    if ((strobe.HardwareValue ?? FirstStrobingValue(channel)) is { } v)
                    {
                        on.SetValue(light.Id, channel.Key, v);
                        off.SetValue(light.Id, channel.Key, v);
                        usedChannel = true;
                    }
                }
            }

            foreach (var channel in light.Strobe)
            {
                if (on.GetValue(light.Id, channel.Key) is null && NonStrobingValue(channel) is { } safe)
                {
                    on.SetValue(light.Id, channel.Key, safe);
                    off.SetValue(light.Id, channel.Key, safe);
                }
            }

            WriteColor(on, light, color.R, color.G, color.B, 1, strobe.UseWhiteChannel);
            if (usedChannel || rate <= 0)
            {
                WriteColor(off, light, color.R, color.G, color.B, 1, strobe.UseWhiteChannel);
            }
            else
            {
                needsSoftware = true;
                WriteColor(off, light, color.R, color.G, color.B, 0, strobe.UseWhiteChannel);
            }
        }

        if (!needsSoftware)
        {
            CopyIdentity(strobe, on);
            return (on, []);
        }

        var chaser = NewChaser(strobe, RunOrder.Loop);
        var onScene = ChaserSteps.NewOwnLookScene(chaser.Id, "On", on.Values);
        var offScene = ChaserSteps.NewOwnLookScene(chaser.Id, "Off", off.Values);
        var duty = Math.Clamp(
            double.IsFinite(strobe.DutyCycle) ? strobe.DutyCycle : StrobeFunction.DefaultDutyCycle,
            StrobeFunction.MinDutyCycle,
            StrobeFunction.MaxDutyCycle);
        var period = 1 / rate;
        chaser.AddStep(onScene.Id, Math.Max(duty * period, FunctionEditing.MinStepSeconds), 0, 0);
        chaser.AddStep(offScene.Id, Math.Max((1 - duty) * period, FunctionEditing.MinStepSeconds), 0, 0);
        return (chaser, [onScene, offScene]);
    }

    /// <summary>Start of the first range the shared classifier calls strobing, or null when the profile documents none.</summary>
    private static byte? FirstStrobingValue(ChannelDefinition channel) =>
        FirstStrobingRange(channel) is { } range ? (byte)Math.Clamp(range.Min, 0, 255) : null;

    /// <summary>
    /// The value a Strobe channel holds while a converted strobe does not use it, resolved from the profile with the shared
    /// <see cref="StrobeCapabilityClassifier"/> (the same rule as the engine's strobe-safe value): the profile default when
    /// it lies in a steady (open / no strobe) range, else the start of the first steady range; without a steady range the
    /// start of the first closed range when the default itself would strobe. Null (channel left alone, at its default)
    /// when the profile documents nothing that says the default strobes.
    /// </summary>
    private static byte? NonStrobingValue(ChannelDefinition channel)
    {
        if (channel.Capabilities is null)
        {
            return null;
        }

        ChannelCapability? firstSteady = null;
        ChannelCapability? firstClosed = null;
        var defaultFound = false;
        var defaultKind = StrobeRangeKind.Unknown;
        foreach (var capability in channel.Capabilities)
        {
            if (capability is null)
            {
                continue;
            }

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
            return (byte)Math.Clamp(firstSteady.Min, 0, 255);
        }

        return defaultKind == StrobeRangeKind.Strobing && firstClosed is not null
            ? (byte)Math.Clamp(firstClosed.Min, 0, 255)
            : null;
    }

    private static ChannelCapability? FirstStrobingRange(ChannelDefinition channel)
    {
        if (channel.Capabilities is null)
        {
            return null;
        }

        foreach (var capability in channel.Capabilities)
        {
            if (capability is not null && StrobeCapabilityClassifier.Classify(capability) == StrobeRangeKind.Strobing)
            {
                return capability;
            }
        }

        return null;
    }

    // ───────────────────────────── Shared ─────────────────────────────

    private static ChaserFunction NewChaser(LightFunction legacy, RunOrder order)
    {
        var chaser = new ChaserFunction { RunOrder = order, Direction = RunDirection.Forward };
        CopyIdentity(legacy, chaser);
        return chaser;
    }

    /// <summary>Same id, name, owners, list flag and copy origin: everything that points at the function keeps working.</summary>
    private static void CopyIdentity(LightFunction legacy, LightFunction target)
    {
        target.Id = legacy.Id;
        target.Name = legacy.Name ?? string.Empty;
        target.OwnerShowId = legacy.OwnerShowId;
        target.OwnerFunctionId = legacy.OwnerFunctionId;
        target.ShowInFunctionList = legacy.ShowInFunctionList;
        target.CopiedFromId = legacy.CopiedFromId;
    }

    private static SceneFunction OwnScene(ChaserFunction chaser, string name) => ChaserSteps.NewOwnLookScene(chaser.Id, name, []);

    private static void WriteLights(SceneFunction scene, List<Light> lights, RgbColor color, bool useWhite)
    {
        foreach (var light in lights)
        {
            WriteColor(scene, light, color.R, color.G, color.B, 1, useWhite);
        }
    }

    /// <summary>
    /// The effect colour policy (<see cref="FixtureEffectFunction"/>), as the engine wrote it: the Dimmer carries the level
    /// (colour at full), else the colour is scaled; Amber/UV 0; White per <paramref name="useWhite"/>; lights with White
    /// but no R/G/B get the brightest component on White.
    /// </summary>
    private static void WriteColor(SceneFunction scene, Light light, double r, double g, double b, double level, bool useWhite)
    {
        var brightest = Math.Max(r, Math.Max(g, b));
        var colorLevel = level;
        if (light.Dimmer.Count > 0)
        {
            Set(scene, light.Id, light.Dimmer, light.HasColor ? 255 * level : brightest * level);
            colorLevel = 1;
        }

        if (!light.HasRgb)
        {
            Set(scene, light.Id, light.White, brightest * colorLevel);
            return;
        }

        var w = 0d;
        if (useWhite && light.White.Count > 0)
        {
            w = Math.Min(r, Math.Min(g, b));
            r -= w;
            g -= w;
            b -= w;
        }

        Set(scene, light.Id, light.Red, r * colorLevel);
        Set(scene, light.Id, light.Green, g * colorLevel);
        Set(scene, light.Id, light.Blue, b * colorLevel);
        Set(scene, light.Id, light.White, w * colorLevel);
        Set(scene, light.Id, light.Amber, 0);
        Set(scene, light.Id, light.Uv, 0);
    }

    private static void Set(SceneFunction scene, Guid fixtureId, List<string> keys, double value)
    {
        var dmx = ToByte(value);
        foreach (var key in keys)
        {
            scene.SetValue(fixtureId, key, dmx);
        }
    }

    /// <summary>Rounds half away from zero and clamps to 0–255 (safety rule 9); NaN becomes 0.</summary>
    private static byte ToByte(double value) =>
        double.IsNaN(value) ? (byte)0 : (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    private static bool SameValues(SceneFunction a, SceneFunction b)
    {
        if (a.Values.Count != b.Values.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Values.Count; i++)
        {
            if (a.Values[i].FixtureId != b.Values[i].FixtureId
                || !string.Equals(a.Values[i].ChannelKey, b.Values[i].ChannelKey, StringComparison.Ordinal)
                || a.Values[i].Value != b.Values[i].Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Finite and ≥ 0, else 0; capped at <see cref="FunctionEditing.MaxSeconds"/> (like the engine).</summary>
    private static double Seconds(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? Math.Min(seconds, FunctionEditing.MaxSeconds) : 0;

    private static double Level(double level, double fallback) => Math.Clamp(double.IsFinite(level) ? level : fallback, 0, 1);

    private static double Wrap(double value, double period)
    {
        var result = value % period;
        return result < 0 ? result + period : result;
    }

    /// <summary>
    /// The effect's lights that exist, have a resolvable profile mode and something to light (with a colour channel when
    /// <paramref name="colorOnly"/>), in order and without duplicates, with their channels resolved by type.
    /// </summary>
    private static List<Light> ResolveLights(Project project, List<Guid>? fixtureIds, bool colorOnly)
    {
        var lights = new List<Light>();
        if (fixtureIds is null)
        {
            return lights;
        }

        var seen = new HashSet<Guid>();
        foreach (var fixtureId in fixtureIds)
        {
            if (!seen.Add(fixtureId) || project.FindFixture(fixtureId) is not { } fixture)
            {
                continue;
            }

            var profile = fixture.ProfileId is null ? null : project.FindProfile(fixture.ProfileId);
            if (profile?.FindMode(fixture.ModeName) is not { } mode || mode.Channels is null)
            {
                continue;
            }

            var light = new Light(fixtureId);
            foreach (var channel in profile.ResolveChannels(mode))
            {
                if (string.IsNullOrWhiteSpace(channel.Key))
                {
                    continue;
                }

                switch (channel.Type)
                {
                    case ChannelType.Dimmer: light.Dimmer.Add(channel.Key); break;
                    case ChannelType.Red: light.Red.Add(channel.Key); break;
                    case ChannelType.Green: light.Green.Add(channel.Key); break;
                    case ChannelType.Blue: light.Blue.Add(channel.Key); break;
                    case ChannelType.White: light.White.Add(channel.Key); break;
                    case ChannelType.Amber: light.Amber.Add(channel.Key); break;
                    case ChannelType.UV: light.Uv.Add(channel.Key); break;
                    case ChannelType.Strobe: light.Strobe.Add(channel); break;
                }
            }

            if (colorOnly ? light.HasColor : light.HasColor || light.Dimmer.Count > 0)
            {
                lights.Add(light);
            }
        }

        return lights;
    }

    /// <summary>One light's effect channels (profile keys), resolved by channel type.</summary>
    private sealed class Light(Guid id)
    {
        public Guid Id { get; } = id;

        public List<string> Dimmer { get; } = [];

        public List<string> Red { get; } = [];

        public List<string> Green { get; } = [];

        public List<string> Blue { get; } = [];

        public List<string> White { get; } = [];

        public List<string> Amber { get; } = [];

        public List<string> Uv { get; } = [];

        public List<ChannelDefinition> Strobe { get; } = [];

        public bool HasRgb => Red.Count > 0 || Green.Count > 0 || Blue.Count > 0;

        public bool HasColor => HasRgb || White.Count > 0;
    }
}
