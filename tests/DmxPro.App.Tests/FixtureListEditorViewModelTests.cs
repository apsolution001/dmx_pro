using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>"Lights" panel of the effect and sequence editors: count text, deleted lights, remove and reorder.</summary>
public sealed class FixtureListEditorViewModelTests
{
    private static readonly Guid Gone = new("00000000-0000-0000-0000-0000000000ff");

    private readonly Project _project = new();
    private readonly IReadOnlyList<Fixture> _pars;
    private readonly List<(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed)> _changes = [];

    public FixtureListEditorViewModelTests()
    {
        _pars = _project.AddFixtures(Rgb(), "3ch", 3, 1, 1, "PAR");
    }

    [Fact]
    public void Load_Empty_HasNoItemsAndZeroLights()
    {
        var vm = Create();

        vm.Load([]);

        Assert.True(vm.HasNoItems);
        Assert.Equal("0 lights", vm.CountText);
    }

    [Fact]
    public void Load_OneLight_CountTextSingular()
    {
        var vm = Create();

        vm.Load([_pars[0].Id]);

        Assert.Equal("1 light", vm.CountText);
    }

    [Fact]
    public void Load_ThreeLights_CountTextPlural()
    {
        var vm = Create();

        vm.Load(_pars.Select(p => p.Id));

        Assert.Equal("3 lights", vm.CountText);
        Assert.Equal(["PAR 1", "PAR 2", "PAR 3"], vm.Items.Select(i => i.Name));
    }

    [Fact]
    public void Load_DeletedLight_ListedAsDeletedLightAndMissing()
    {
        var vm = Create();

        vm.Load([_pars[0].Id, Gone]);

        var item = vm.Items[1];
        Assert.Equal("(deleted light)", item.Name);
        Assert.True(item.IsMissing);
        Assert.False(vm.Items[0].IsMissing);
    }

    [Fact]
    public void MoveDown_SelectedLight_MovesLaterAndReportsReorder()
    {
        var vm = Create();
        vm.Load(_pars.Select(p => p.Id));
        vm.SelectedItem = vm.Items[0];

        vm.MoveDownCommand.Execute(null);

        Assert.Equal([_pars[1].Id, _pars[0].Id, _pars[2].Id], vm.FixtureIds);
        Assert.Equal(_pars[0].Id, vm.SelectedItem?.Id);
        var change = Assert.Single(_changes);
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void RemoveSelected_DeletedLight_RemovesItAndReportsRemoval()
    {
        var vm = Create();
        vm.Load([_pars[0].Id, Gone]);
        vm.SelectedItem = vm.Items[1];

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Equal([_pars[0].Id], vm.FixtureIds);
        Assert.Equal("1 light", vm.CountText);
        Assert.Equal([Gone], Assert.Single(_changes).Removed);
    }

    private FixtureListEditorViewModel Create() => new(_project, (added, removed) => _changes.Add((added, removed)));

    private static FixtureProfile Rgb() => new()
    {
        Id = "test-rgb",
        Manufacturer = "Test",
        Model = "RGB",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
        ],
        Modes = [new() { Name = "3ch", Channels = ["r", "g", "b"] }],
    };
}
