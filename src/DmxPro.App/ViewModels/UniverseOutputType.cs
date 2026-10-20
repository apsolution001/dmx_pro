namespace DmxPro.App.ViewModels;

/// <summary>What a universe row on the Input / Output page sends through ("None" = not sent anywhere).</summary>
public enum UniverseOutputType
{
    None,
    Virtual,
    ArtNet,
    Sacn,
    EnttecPro,
    OpenDmx,
}
