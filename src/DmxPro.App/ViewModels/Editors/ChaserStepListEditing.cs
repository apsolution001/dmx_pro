using System.Collections.ObjectModel;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Pure list logic of the chaser editor's step list, kept out of the ViewModel so it can be unit-tested: moving a
/// (possibly scattered) selection up / down or to a drop position, where copies and new steps go, which step to select
/// after a removal, the play order of one cycle, and setting the same timing on several steps. Indexes are 0-based
/// list positions (the UI shows step numbers from 1).
/// </summary>
public static class ChaserStepListEditing
{
    /// <summary>
    /// The new order after moving every selected item by <paramref name="offset"/> places (-1 up, +1 down). The
    /// selection moves as a group: when any selected item already touches the end it moves towards, nothing moves.
    /// Returns null when the order does not change. Result: <c>order[newPosition] = oldIndex</c>.
    /// </summary>
    public static int[]? MoveBy(int count, IReadOnlyCollection<int> selected, int offset)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var picked = Normalize(count, selected);
        if (picked.Count == 0 || picked.Count == count || offset == 0)
        {
            return null;
        }

        var first = picked[0];
        var last = picked[^1];
        if ((offset < 0 && first + offset < 0) || (offset > 0 && last + offset > count - 1))
        {
            return null;
        }

        var order = Enumerable.Range(0, count).ToArray();
        var isPicked = new bool[count];
        foreach (var index in picked)
        {
            isPicked[index] = true;
        }

        // Bubble each selected item one place at a time, starting with the one nearest the target end, so the
        // unselected items keep their relative order.
        for (var step = 0; step < Math.Abs(offset); step++)
        {
            if (offset < 0)
            {
                for (var i = 1; i < count; i++)
                {
                    if (isPicked[order[i]] && !isPicked[order[i - 1]])
                    {
                        (order[i], order[i - 1]) = (order[i - 1], order[i]);
                    }
                }
            }
            else
            {
                for (var i = count - 2; i >= 0; i--)
                {
                    if (isPicked[order[i]] && !isPicked[order[i + 1]])
                    {
                        (order[i], order[i + 1]) = (order[i + 1], order[i]);
                    }
                }
            }
        }

        return IsIdentity(order) ? null : order;
    }

    /// <summary>
    /// The new order after dropping the selected items (kept in their order, as one block) into the gap before
    /// <paramref name="insertBefore"/> (0 = top, <paramref name="count"/> = after the last item; positions are in the
    /// old list). Returns null when the order does not change.
    /// </summary>
    public static int[]? MoveTo(int count, IReadOnlyCollection<int> selected, int insertBefore)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var picked = Normalize(count, selected);
        if (picked.Count == 0)
        {
            return null;
        }

        var gap = Math.Clamp(insertBefore, 0, count);
        var rest = Enumerable.Range(0, count).Where(i => picked.BinarySearch(i) < 0).ToList();
        var position = rest.Count(i => i < gap);
        rest.InsertRange(position, picked);
        var order = rest.ToArray();
        return IsIdentity(order) ? null : order;
    }

    /// <summary>Applies an order from <see cref="MoveBy"/> / <see cref="MoveTo"/> with moves only (rows keep their UI).</summary>
    public static void ApplyOrder<T>(ObservableCollection<T> items, IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(order);
        if (order.Count != items.Count)
        {
            throw new ArgumentException("The order must list every item once.", nameof(order));
        }

        var wanted = order.Select(i => items[i]).ToList();
        for (var position = 0; position < wanted.Count; position++)
        {
            var current = items.IndexOf(wanted[position]);
            if (current != position)
            {
                items.Move(current, position);
            }
        }
    }

    /// <summary>Where new or copied steps go: right after the last selected step, or at the end without a selection.</summary>
    public static int InsertIndex(int count, IReadOnlyCollection<int> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var picked = Normalize(count, selected);
        return picked.Count == 0 ? count : picked[^1] + 1;
    }

    /// <summary>
    /// Which index to select after removing <paramref name="selected"/> from a list of <paramref name="count"/>: the
    /// step that now sits where the first removed one was (or the new last step). -1 when the list becomes empty.
    /// </summary>
    public static int SelectionAfterRemove(int count, IReadOnlyCollection<int> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var picked = Normalize(count, selected);
        var remaining = count - picked.Count;
        if (remaining <= 0)
        {
            return -1;
        }

        return picked.Count == 0 ? -1 : Math.Min(picked[0], remaining - 1);
    }

    /// <summary>
    /// The step indexes of one cycle in the order they play (the same order <see cref="FunctionEditing"/>.CycleSeconds
    /// counts): forward 0…n-1 or backward n-1…0; back and forth adds the way back without repeating the end steps.
    /// </summary>
    public static IReadOnlyList<int> PlayOrder(int count, RunOrder order, RunDirection direction)
    {
        var result = new List<int>(Math.Max(0, count * 2));
        for (var i = 0; i < count; i++)
        {
            result.Add(direction == RunDirection.Backward ? count - 1 - i : i);
        }

        if (order == RunOrder.PingPong && count >= 3)
        {
            for (var i = count - 2; i >= 1; i--)
            {
                result.Add(result[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Sets the same hold / fade in / fade out on every step (each rounded to milliseconds and clamped to
    /// 0–<see cref="FunctionEditing.MaxSeconds"/>). Returns how many steps actually changed.
    /// </summary>
    public static int ApplyTiming(IEnumerable<ChaserStep> steps, double holdSeconds, double fadeInSeconds, double fadeOutSeconds)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var hold = FunctionEditing.Clamp(holdSeconds, 0, FunctionEditing.MaxSeconds);
        var fadeIn = FunctionEditing.Clamp(fadeInSeconds, 0, FunctionEditing.MaxSeconds);
        var fadeOut = FunctionEditing.Clamp(fadeOutSeconds, 0, FunctionEditing.MaxSeconds);
        var changed = 0;
        foreach (var step in steps)
        {
            if (step.HoldSeconds == hold && step.FadeInSeconds == fadeIn && step.FadeOutSeconds == fadeOut)
            {
                continue;
            }

            step.HoldSeconds = hold;
            step.FadeInSeconds = fadeIn;
            step.FadeOutSeconds = fadeOut;
            changed++;
        }

        return changed;
    }

    // Valid, distinct, sorted indexes.
    private static List<int> Normalize(int count, IReadOnlyCollection<int> selected) =>
        selected.Where(i => i >= 0 && i < count).Distinct().Order().ToList();

    private static bool IsIdentity(int[] order)
    {
        for (var i = 0; i < order.Length; i++)
        {
            if (order[i] != i)
            {
                return false;
            }
        }

        return true;
    }
}
