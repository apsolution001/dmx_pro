using DmxPro.App.ViewModels;

namespace DmxPro.App.Tests;

/// <summary>"Choose lights" dialog: picked set, group tri-state and the merge into the function's list, with a search.</summary>
public sealed class FixturePickerViewModelTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");
    private static readonly Guid Gone = new("00000000-0000-0000-0000-0000000000ff");
    private static readonly Guid FrontGroup = new("00000000-0000-0000-0000-000000000100");

    [Fact]
    public void Constructor_ExistingLights_StartPickedAndGroupIndeterminate()
    {
        var vm = Create(existing: [A]);

        Assert.Equal(1, vm.PickedCount);
        Assert.Null(Front(vm).IsChecked);
        Assert.Equal("No changes", vm.ChangeSummary);
    }

    [Fact]
    public void SearchText_FiltersVisibleLightsByNameKeepingPickedSet()
    {
        var vm = Create(existing: [A, C]);

        vm.SearchText = "back";

        Assert.Equal([C, D], vm.VisibleLights.Select(l => l.Id));
        Assert.Equal(2, vm.PickedCount);
    }

    [Fact]
    public void SearchText_NoMatch_HasNoMatches()
    {
        var vm = Create(existing: []);

        vm.SearchText = "zzz";

        Assert.True(vm.HasNoMatches);
    }

    [Fact]
    public void Clear_WithSearch_UnpicksOnlyVisibleLights()
    {
        var vm = Create(existing: [A, B, C]);
        vm.SearchText = "back";

        vm.ClearCommand.Execute(null);

        Assert.Equal([A, B], vm.PickedIds);
        Assert.True(Front(vm).IsChecked);
        Assert.Equal([A, B], vm.Result);
    }

    [Fact]
    public void SelectAll_WithSearch_PicksOnlyVisibleLights()
    {
        var vm = Create(existing: []);
        vm.SearchText = "front";

        vm.SelectAllCommand.Execute(null);

        Assert.Equal([A, B], vm.PickedIds);
        Assert.True(Front(vm).IsChecked);
    }

    [Fact]
    public void GroupTick_MembersHiddenBySearch_PicksEveryMember()
    {
        var vm = Create(existing: [A]);
        vm.SearchText = "back";

        Front(vm).IsChecked = true;

        Assert.Equal([A, B], vm.PickedIds);
        Assert.True(Front(vm).IsChecked);
        Assert.Equal([A, B], vm.Result);
    }

    [Fact]
    public void GroupUntick_MembersHiddenBySearch_UnpicksEveryMember()
    {
        var vm = Create(existing: [A, B, C]);
        vm.SearchText = "back";

        Front(vm).IsChecked = false;

        Assert.Equal([C], vm.PickedIds);
        Assert.False(Front(vm).IsChecked);
    }

    [Fact]
    public void GroupIndeterminateClicked_TreatedAsUnpickAll()
    {
        var vm = Create(existing: [A, B]);

        Front(vm).IsChecked = null;

        Assert.Empty(vm.PickedIds);
        Assert.False(Front(vm).IsChecked);
    }

    [Fact]
    public void GroupTick_RaisesSelectionResetForTheView()
    {
        var vm = Create(existing: []);
        var resets = 0;
        vm.SelectionReset += (_, _) => resets++;

        Front(vm).IsChecked = true;

        Assert.Equal(1, resets);
    }

    [Fact]
    public void ApplyListSelection_WithSearch_UpdatesGroupState()
    {
        var vm = Create(existing: []);
        vm.SearchText = "front";

        vm.ApplyListSelection(new[] { Light(vm, A) }, Array.Empty<object>());
        var oneOfTwo = Front(vm).IsChecked;
        vm.ApplyListSelection(new[] { Light(vm, B) }, Array.Empty<object>());

        Assert.Null(oneOfTwo);
        Assert.True(Front(vm).IsChecked);
        Assert.Equal("2 to add", vm.ChangeSummary);
    }

    [Fact]
    public void SearchChange_ListRebuildDoesNotUnpickHiddenLights()
    {
        var vm = Create(existing: [A, C]);
        var a = Light(vm, A);

        // The list drops its selection while the visible lights are rebuilt; that is not the user unpicking.
        vm.VisibleLights.CollectionChanged += (_, _) => vm.ApplyListSelection(Array.Empty<object>(), new[] { a });
        vm.SearchText = "back";

        Assert.Equal([A, C], vm.PickedIds);
    }

    [Fact]
    public void Result_KeepsExistingOrderAndAppendsNewInPatchOrder()
    {
        var vm = Create(existing: [C, A]);

        vm.ApplyListSelection(new[] { Light(vm, D), Light(vm, B) }, Array.Empty<object>());

        Assert.Equal([C, A, B, D], vm.Result);
    }

    [Fact]
    public void Result_UnpickedRemovedAndUnpatchedExistingKept()
    {
        var vm = Create(existing: [Gone, C, A]);

        vm.ApplyListSelection(Array.Empty<object>(), new[] { Light(vm, C) });

        Assert.Equal([Gone, A], vm.Result);
        Assert.Equal("1 to remove", vm.ChangeSummary);
    }

    [Fact]
    public void Groups_UnpatchedMembersIgnored()
    {
        var vm = new FixturePickerViewModel(
            "t",
            ordered: true,
            Lights([]),
            [(FrontGroup, "Front", (IReadOnlyList<Guid>)[A, Gone, A])],
            []);

        Assert.Equal([A], Front(vm).MemberIds);
    }

    private static FixturePickerGroup Front(FixturePickerViewModel vm) => vm.Groups.Single(g => g.Id == FrontGroup);

    private static FixturePickerLight Light(FixturePickerViewModel vm, Guid id) => vm.VisibleLights.Single(l => l.Id == id);

    private static FixturePickerViewModel Create(Guid[] existing) => new(
        "Choose lights",
        ordered: true,
        Lights(existing),
        [(FrontGroup, "Front", (IReadOnlyList<Guid>)[A, B])],
        existing);

    private static List<FixturePickerLight> Lights(Guid[] used) =>
    [
        new(A, 1, "Front left", "U1 · 1–4", "RGBW Par", "4ch", used.Contains(A)),
        new(B, 2, "Front right", "U1 · 5–8", "RGBW Par", "4ch", used.Contains(B)),
        new(C, 3, "Back left", "U1 · 9–12", "RGBW Par", "4ch", used.Contains(C)),
        new(D, 4, "Back right", "U1 · 13–16", "RGBW Par", "4ch", used.Contains(D)),
    ];
}
