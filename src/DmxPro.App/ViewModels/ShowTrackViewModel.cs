using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Shows;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One lane of the show timeline: header (name, inline rename with F2/Enter/Esc, delete) and its item blocks. The
/// time-range selection and drag-and-drop highlight are drawn per lane from <see cref="HasRange"/> and
/// <see cref="IsDropTarget"/>.
/// </summary>
public sealed partial class ShowTrackViewModel : ObservableObject
{
    private readonly ShowsViewModel _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private bool _hasRange;

    [ObservableProperty]
    private double _rangeLeft;

    [ObservableProperty]
    private double _rangeWidth;

    public ShowTrackViewModel(ShowsViewModel owner, ShowTrack track)
    {
        _owner = owner;
        Track = track;
    }

    public ShowTrack Track { get; }

    public string Name => Track.Name;

    public bool IsNotEditing => !IsEditing;

    public ObservableCollection<ShowItemViewModel> Items { get; } = [];

    public void RefreshName() => OnPropertyChanged(nameof(Name));

    [RelayCommand]
    private void BeginRename()
    {
        EditName = Track.Name;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        _owner.RenameTrack(this, EditName);
    }

    [RelayCommand]
    private void CancelRename() => IsEditing = false;

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteTrackAsync(this);

    [RelayCommand]
    private void Activate() => _owner.SetActiveTrack(this);
}
