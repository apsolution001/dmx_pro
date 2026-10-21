using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One patched light in the "Choose lights" dialog: patch number, name, address, profile and mode, whether it is
/// picked and whether the function used it before (so the row can say "Add" or "Remove").
/// </summary>
public sealed partial class FixturePickerLight : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeText))]
    private bool _isPicked;

    public FixturePickerLight(Guid id, int number, string name, string address, string profile, string mode, bool wasUsed)
    {
        Id = id;
        Number = number;
        Name = name;
        Address = address;
        Profile = profile;
        Mode = mode;
        WasUsed = wasUsed;
        _isPicked = wasUsed;
    }

    public Guid Id { get; }

    /// <summary>Position in the patch, from 1.</summary>
    public int Number { get; }

    public string Name { get; }

    /// <summary>"U1 · 1–8".</summary>
    public string Address { get; }

    public string Profile { get; }

    public string Mode { get; }

    /// <summary>The function used this light when the dialog opened.</summary>
    public bool WasUsed { get; }

    /// <summary>"Add" for a newly picked light, "Remove" for a used light that was unpicked, else empty.</summary>
    public string ChangeText => IsPicked == WasUsed ? string.Empty : IsPicked ? "Add" : "Remove";

    public string AutomationName => $"{Number}. {Name}, {Address}, {Profile}, {Mode}";

    /// <summary>Text the search box matches against.</summary>
    internal string SearchKey => $"{Number} {Name} {Address} {Profile} {Mode}";
}
