namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// A pick-one row of <see cref="EffectChoice{T}"/> buttons. Choosing a button calls the apply action (the editor sets its
/// value); the editor then calls <see cref="Sync"/> with its stored value, which checks the matching button (or none).
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class EffectChoiceGroup<T>
{
    private readonly Action<T> _apply;
    private readonly Func<T, T, bool> _matches;

    public EffectChoiceGroup(IEnumerable<(T Value, string Label, string ToolTip)> choices, Action<T> apply, Func<T, T, bool>? matches = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
        _matches = matches ?? EqualityComparer<T>.Default.Equals;
        GroupName = "EffectChoice-" + Guid.NewGuid().ToString("N");
        Choices = choices.Select(c => new EffectChoice<T>(c.Value, c.Label, c.ToolTip, GroupName, Choose)).ToList();
    }

    /// <summary>
    /// Id shared by this group's buttons and no other group (a radio button GroupName), so the buttons of one row form
    /// one group for keyboard navigation and screen readers even though each sits in its own item container.
    /// </summary>
    public string GroupName { get; }

    public IReadOnlyList<EffectChoice<T>> Choices { get; }

    /// <summary>The checked choice; null when the value matches none (a custom value).</summary>
    public EffectChoice<T>? Selected => Choices.FirstOrDefault(c => c.IsSelected);

    /// <summary>Checks the first choice that matches <paramref name="value"/> and unchecks the rest.</summary>
    public void Sync(T value)
    {
        var found = false;
        foreach (var choice in Choices)
        {
            var match = !found && _matches(choice.Value, value);
            found |= match;
            choice.SetSelected(match);
        }
    }

    private void Choose(EffectChoice<T> choice) => _apply(choice.Value);
}
