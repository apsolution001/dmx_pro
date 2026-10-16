using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>One square of the universe address map.</summary>
public sealed partial class AddressCellViewModel : ObservableObject
{
    [ObservableProperty]
    private AddressCellState _state;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _toolTip = string.Empty;

    public AddressCellViewModel(int address)
    {
        Address = address;
    }

    public int Address { get; }
}
