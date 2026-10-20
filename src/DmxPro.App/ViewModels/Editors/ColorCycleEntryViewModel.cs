using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>One colour of the color cycle editor's ordered colour list.</summary>
public sealed partial class ColorCycleEntryViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hex), nameof(AutomationName))]
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

    public string AutomationName => $"Colour {Number}, {Hex}";
}
