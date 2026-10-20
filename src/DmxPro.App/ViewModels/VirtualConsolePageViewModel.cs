using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.App.ViewModels;

/// <summary>One console page tab: its name (with inline rename state), the Buttons ⇄ Sliders switch and its widgets.</summary>
public sealed partial class VirtualConsolePageViewModel : ObservableObject
{
    private readonly VirtualConsoleViewModel _console;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _editName = string.Empty;

    public VirtualConsolePageViewModel(VirtualConsoleViewModel console, ConsolePage model)
    {
        _console = console;
        Model = model;
        foreach (var widget in model.Widgets)
        {
            Widgets.Add(new ConsoleWidgetViewModel(console, this, widget));
        }
    }

    public ConsolePage Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public int Columns => Model.Columns;

    public int Rows => Model.Rows;

    public ObservableCollection<ConsoleWidgetViewModel> Widgets { get; } = [];

    /// <summary>Owner request: show every function button as a fader of the same colour (and back). Saved with the page.</summary>
    public bool ShowButtonsAsSliders
    {
        get => Model.ShowButtonsAsSliders;
        set
        {
            if (value != Model.ShowButtonsAsSliders)
            {
                _console.SetButtonsAsSliders(this, value);
                OnPropertyChanged();
            }
        }
    }

    internal void RaiseNameChanged() => OnPropertyChanged(nameof(Name));
}
