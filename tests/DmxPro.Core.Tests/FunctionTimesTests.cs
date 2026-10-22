using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class FunctionTimesTests
{
    [Fact]
    public void ClampTimes_ChaserStepsOutOfRange_ClampedToEditorRange()
    {
        var chaser = new ChaserFunction { Name = "Damaged" };
        var step = chaser.AddStep(Guid.NewGuid(), holdSeconds: 1e308, fadeInSeconds: -5, fadeOutSeconds: double.NaN);

        Assert.True(chaser.ClampTimes());

        Assert.Equal(FunctionEditing.MaxSeconds, step.HoldSeconds);
        Assert.Equal(0, step.FadeInSeconds);
        Assert.Equal(0, step.FadeOutSeconds);
    }

    [Fact]
    public void ClampTimes_ValidTimes_UnchangedAndNotRounded()
    {
        var scene = new SceneFunction { Name = "S", FadeInSeconds = 0.12345, FadeOutSeconds = FunctionEditing.MaxSeconds };
        var cycle = new ColorCycleFunction { Name = "C", HoldSeconds = 0, FadeSeconds = 2.5, FixtureOffsetSeconds = 0.1 };

        Assert.False(scene.ClampTimes());
        Assert.False(cycle.ClampTimes());

        Assert.Equal(0.12345, scene.FadeInSeconds);
        Assert.Equal(FunctionEditing.MaxSeconds, scene.FadeOutSeconds);
        Assert.Equal(2.5, cycle.FadeSeconds);
    }

    [Fact]
    public void ClampTimes_EveryTimedType_ClampsEachField()
    {
        var sequence = new SequenceFunction { Name = "Seq" };
        sequence.Steps.Add(new SequenceStep { FadeInSeconds = double.PositiveInfinity, HoldSeconds = -1, FadeOutSeconds = 4000 });
        var cycle = new ColorCycleFunction { Name = "C", HoldSeconds = 1e300, FadeSeconds = double.NaN, FixtureOffsetSeconds = -2 };
        var fade = new FadeFunction { Name = "F", DurationSeconds = 1e9 };
        var flash = new FlashFunction { Name = "Fl", DurationSeconds = -1 };
        var scene = new SceneFunction { Name = "S", FadeInSeconds = double.NegativeInfinity, FadeOutSeconds = 7200 };

        Assert.True(sequence.ClampTimes());
        Assert.True(cycle.ClampTimes());
        Assert.True(fade.ClampTimes());
        Assert.True(flash.ClampTimes());
        Assert.True(scene.ClampTimes());

        Assert.Equal((FunctionEditing.MaxSeconds, 0d, FunctionEditing.MaxSeconds), (sequence.Steps[0].FadeInSeconds, sequence.Steps[0].HoldSeconds, sequence.Steps[0].FadeOutSeconds));
        Assert.Equal((FunctionEditing.MaxSeconds, 0d, 0d), (cycle.HoldSeconds, cycle.FadeSeconds, cycle.FixtureOffsetSeconds));
        Assert.Equal(FunctionEditing.MaxSeconds, fade.DurationSeconds);
        Assert.Equal(0, flash.DurationSeconds);
        Assert.Equal((0d, FunctionEditing.MaxSeconds), (scene.FadeInSeconds, scene.FadeOutSeconds));
    }

    [Theory]
    [InlineData(double.NaN, PulseFunction.DefaultPeriodSeconds)]
    [InlineData(0.0, PulseFunction.MinPeriodSeconds)]
    [InlineData(1e308, FunctionEditing.MaxSeconds)]
    [InlineData(2.0, 2.0)]
    public void ClampTimes_PulsePeriod_ClampedToPulseRange(double period, double expected)
    {
        var pulse = new PulseFunction { Name = "P", PeriodSeconds = period };

        pulse.ClampTimes();

        Assert.Equal(expected, pulse.PeriodSeconds);
    }

    [Fact]
    public void Deserialize_ChaserWithHugeStepTimes_ClampedOnLoad()
    {
        var serializer = new ProjectSerializer();
        var project = new Project();
        var chaser = new ChaserFunction { Name = "Damaged" };
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 1e308, fadeInSeconds: -3);
        project.Functions.Add(chaser);

        var loaded = serializer.Deserialize(serializer.Serialize(project)).Project;

        var step = Assert.IsType<ChaserFunction>(Assert.Single(loaded.Functions)).Steps[0];
        Assert.Equal(FunctionEditing.MaxSeconds, step.HoldSeconds);
        Assert.Equal(0, step.FadeInSeconds);
    }
}
