namespace DmxPro.Core.Shows;

/// <summary>One row of a show timeline.</summary>
public sealed class ShowTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public List<ShowItem> Items { get; set; } = [];

    public ShowItem? FindItem(Guid itemId) => Items.FirstOrDefault(i => i.Id == itemId);
}
