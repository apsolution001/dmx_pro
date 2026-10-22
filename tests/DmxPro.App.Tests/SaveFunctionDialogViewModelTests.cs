using DmxPro.App.ViewModels;

namespace DmxPro.App.Tests;

public sealed class SaveFunctionDialogViewModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Name_Blank_CannotSaveAndShowsError(string name)
    {
        var vm = Create(name);

        Assert.False(vm.CanSave);
        Assert.False(vm.TryAccept());
        Assert.True(vm.HasNameError);
        Assert.Equal("Enter a name.", vm.NameError);
        Assert.False(vm.HasNameWarning);
    }

    [Fact]
    public void Name_Null_TreatedAsBlank()
    {
        var vm = Create("Red");

        vm.Name = null!;

        Assert.Equal(string.Empty, vm.TrimmedName);
        Assert.False(vm.CanSave);
        Assert.True(vm.HasNameError);
    }

    [Fact]
    public void Name_SurroundingSpaces_TrimmedAndValid()
    {
        var vm = Create("  Red wash  ");

        Assert.Equal("Red wash", vm.TrimmedName);
        Assert.True(vm.CanSave);
        Assert.True(vm.TryAccept());
        Assert.Null(vm.NameError);
    }

    [Fact]
    public void Name_Taken_WarnsButCanStillSave()
    {
        var vm = Create("  Red  ", taken: "Red");

        Assert.True(vm.HasNameWarning);
        Assert.Equal("A function called \"Red\" already exists. Saving adds a second one.", vm.NameWarning);
        Assert.True(vm.CanSave);
        Assert.True(vm.TryAccept());
    }

    [Fact]
    public void Name_TakenCheck_GetsTheTrimmedName()
    {
        var asked = new List<string>();
        var vm = new SaveFunctionDialogViewModel("t", "m", "  Blue  ", false, n =>
        {
            asked.Add(n);
            return false;
        });

        _ = vm.NameWarning;

        Assert.Equal(["Blue"], asked);
    }

    [Fact]
    public void Name_Blank_NeverAsksWhetherTaken()
    {
        var asked = 0;
        var vm = new SaveFunctionDialogViewModel("t", "m", "  ", false, _ =>
        {
            asked++;
            return true;
        });

        Assert.Null(vm.NameWarning);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Name_Changed_RaisesDependentProperties()
    {
        var vm = Create("Red");
        var changed = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Name = "Blue";

        Assert.Superset(
            new HashSet<string?>
            {
                nameof(vm.TrimmedName),
                nameof(vm.NameError),
                nameof(vm.HasNameError),
                nameof(vm.NameWarning),
                nameof(vm.HasNameWarning),
                nameof(vm.CanSave),
            },
            changed);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-2.0, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(1.5, 1.5)]
    public void FadeIn_InvalidOrNegative_IsZero(double? input, double expected)
    {
        var vm = Create("Red");

        vm.FadeInSeconds = input;
        vm.FadeOutSeconds = input;

        Assert.Equal(expected, vm.FadeIn);
        Assert.Equal(expected, vm.FadeOut);
    }

    private static SaveFunctionDialogViewModel Create(string name, string? taken = null) =>
        new("Save as function", "message", name, showFades: true, n => n == taken);
}
