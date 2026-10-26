using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>One colour of the color cycle editor's ordered colour list: number, swatch, plain name and hex.</summary>
public sealed partial class ColorCycleEntryViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hex), nameof(Name), nameof(AutomationName))]
    private RgbColor _color;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private int _number;

    public ColorCycleEntryViewModel(RgbColor color, int number)
    {
        _color = color;
        _number = number;
    }

    /// <summary>"#RRGGBB" (a real light colour, used for the swatch).</summary>
    public string Hex => Color.ToHex();

    /// <summary>Plain colour name ("Warm white", "Orange"), so the list does not rely on the swatch alone.</summary>
    public string Name => ColorCycleColorNames.NameOf(Color);

    public string AutomationName => $"Colour {Number}, {Name}, {Hex}";
}
