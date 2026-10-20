namespace DmxPro.Core.VirtualConsole;

/// <summary>Level slider for a fixture group (a single fixture is controlled through a group of one).</summary>
public sealed class GroupLevelWidget : ConsoleWidget
{
    public GroupLevelWidget()
    {
        Height = 4;
    }

    /// <summary>The group this slider controls; <see cref="Guid.Empty"/> while not assigned yet.</summary>
    public Guid GroupId { get; set; }
}
