using DmxPro.Core.Patching;

namespace DmxPro.Core.Tests;

/// <summary>Rules of the "choose lights" dialog (<see cref="FixtureSelection"/>).</summary>
public class FixtureSelectionTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();
    private static readonly Guid Deleted = Guid.NewGuid();

    private static readonly Guid[] Patch = [A, B, C, D];

    [Fact]
    public void Merge_EmptyList_ReturnsPickedInPatchOrder()
    {
        var result = FixtureSelection.Merge([], Patch, new HashSet<Guid> { D, B });

        Assert.Equal([B, D], result);
    }

    [Fact]
    public void Merge_ExistingOrder_IsKeptAndNewOnesAppendedInPatchOrder()
    {
        var result = FixtureSelection.Merge([C, A], Patch, new HashSet<Guid> { A, B, C, D });

        Assert.Equal([C, A, B, D], result);
    }

    [Fact]
    public void Merge_UnpickedExisting_IsRemoved()
    {
        var result = FixtureSelection.Merge([C, A, B], Patch, new HashSet<Guid> { A, D });

        Assert.Equal([A, D], result);
    }

    [Fact]
    public void Merge_NothingPicked_ReturnsEmpty()
    {
        var result = FixtureSelection.Merge([A, B], Patch, new HashSet<Guid>());

        Assert.Empty(result);
    }

    [Fact]
    public void Merge_FixtureDeletedFromPatch_IsKeptInPlace()
    {
        var result = FixtureSelection.Merge([A, Deleted, B], Patch, new HashSet<Guid> { B, C });

        Assert.Equal([Deleted, B, C], result);
    }

    [Fact]
    public void Merge_DuplicatesInExisting_AreDropped()
    {
        var result = FixtureSelection.Merge([B, B, A], Patch, new HashSet<Guid> { A, B });

        Assert.Equal([B, A], result);
    }

    [Fact]
    public void Merge_PickedIdNotInPatch_IsIgnored()
    {
        var result = FixtureSelection.Merge([], Patch, new HashSet<Guid> { Deleted, A });

        Assert.Equal([A], result);
    }

    [Fact]
    public void GroupState_AllMembersPicked_ReturnsTrue() =>
        Assert.True(FixtureSelection.GroupState([A, B], new HashSet<Guid> { A, B, C }));

    [Fact]
    public void GroupState_NoMemberPicked_ReturnsFalse() =>
        Assert.False(FixtureSelection.GroupState([A, B], new HashSet<Guid> { C }));

    [Fact]
    public void GroupState_SomeMembersPicked_ReturnsNull() =>
        Assert.Null(FixtureSelection.GroupState([A, B], new HashSet<Guid> { B }));

    [Fact]
    public void GroupState_EmptyGroup_ReturnsFalse() =>
        Assert.False(FixtureSelection.GroupState([], new HashSet<Guid> { A }));

    [Fact]
    public void Added_NewIds_ReturnedInAfterOrder() =>
        Assert.Equal([D, B], FixtureSelection.Added([A, C], [A, D, C, B]));

    [Fact]
    public void Added_SwappedArguments_ReturnsRemovedIds() =>
        Assert.Equal([B, D], FixtureSelection.Added(before: [A, C], after: [B, A, C, D]));

    [Fact]
    public void Added_DuplicateInAfter_ReturnedOnce() =>
        Assert.Equal([B], FixtureSelection.Added([A], [B, A, B]));

    [Fact]
    public void Added_NothingNew_ReturnsEmpty() =>
        Assert.Empty(FixtureSelection.Added([B, A, C, D], [A, C]));
}
