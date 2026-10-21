namespace DmxPro.Core.Patching;

/// <summary>
/// Rules of the "choose lights" dialog, kept out of the UI so they are testable: how a picked set of fixtures merges
/// into a function's ordered fixture list, and how a group's tick follows the picked lights.
/// </summary>
public static class FixtureSelection
{
    /// <summary>
    /// The function's new fixture list after a pick. Fixtures already in <paramref name="existing"/> keep their order;
    /// those the user unpicked are dropped; newly picked ones are appended in <paramref name="patchOrder"/>.
    /// Entries of <paramref name="existing"/> that are not in <paramref name="patchOrder"/> (deleted from the patch)
    /// could not be shown in the picker, so they are kept; the list removes them explicitly. Duplicates are dropped.
    /// </summary>
    /// <param name="existing">The function's fixture ids, in play order.</param>
    /// <param name="patchOrder">Every patched fixture id, in patch order (the picker's list order).</param>
    /// <param name="picked">The fixture ids picked in the dialog.</param>
    public static IReadOnlyList<Guid> Merge(IReadOnlyList<Guid> existing, IReadOnlyList<Guid> patchOrder, IReadOnlySet<Guid> picked)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(patchOrder);
        ArgumentNullException.ThrowIfNull(picked);

        var patched = new HashSet<Guid>(patchOrder);
        var result = new List<Guid>(Math.Max(existing.Count, picked.Count));
        var seen = new HashSet<Guid>();
        foreach (var id in existing)
        {
            if ((picked.Contains(id) || !patched.Contains(id)) && seen.Add(id))
            {
                result.Add(id);
            }
        }

        foreach (var id in patchOrder)
        {
            if (picked.Contains(id) && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// Tick state of a group: true when every member is picked, false when none is (or the group is empty), null
    /// (indeterminate) when only some are.
    /// </summary>
    public static bool? GroupState(IReadOnlyList<Guid> members, IReadOnlySet<Guid> picked)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(picked);

        var count = 0;
        foreach (var id in members)
        {
            if (picked.Contains(id))
            {
                count++;
            }
        }

        return count == 0 ? false : count == members.Count ? true : null;
    }

    /// <summary>The ids in <paramref name="after"/> but not in <paramref name="before"/>, in <paramref name="after"/> order.</summary>
    public static IReadOnlyList<Guid> Added(IReadOnlyList<Guid> before, IReadOnlyList<Guid> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var old = new HashSet<Guid>(before);
        var result = new List<Guid>();
        foreach (var id in after)
        {
            if (old.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}
