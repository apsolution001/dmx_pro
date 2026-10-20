using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.App.Services;
using DmxPro.Core.Shows;

namespace DmxPro.App.ViewModels;

/// <summary>One show in the Virtual Console "Shows" list: summary columns, play state and faint timeline blocks.</summary>
public sealed partial class ShowListItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LengthText), nameof(LengthPreciseText))]
    private double _lengthSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BpmText), nameof(DetailsText))]
    private double _bpm;

    [ObservableProperty]
    private int _trackCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private int _itemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(StateText), nameof(PlayIcon), nameof(PlayToolTip))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(StateText), nameof(PlayIcon), nameof(PlayToolTip))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isMuted;

    [ObservableProperty]
    private IReadOnlyList<ShowTimelineBlock> _blocks = [];

    public ShowListItemViewModel(Show show)
    {
        Id = show.Id;
        Update(show);
    }

    public Guid Id { get; }

    /// <summary>Length as m:ss (list column).</summary>
    public string LengthText => ShowPlayerViewModel.FormatLength(LengthSeconds);

    /// <summary>Length as m:ss.t (transport bar).</summary>
    public string LengthPreciseText => ShowPlayerViewModel.FormatPosition(LengthSeconds);

    public string BpmText => Bpm.ToString("0.#", CultureInfo.CurrentCulture);

    /// <summary>Second line in the transport bar, e.g. "120 BPM · 12 items".</summary>
    public string DetailsText => $"{BpmText} BPM · {ItemCount} item{(ItemCount == 1 ? string.Empty : "s")}";

    public bool IsPlaying => IsRunning && !IsPaused;

    public string StateText =>
        !IsRunning ? string.Empty
        : IsPaused ? "Paused"
        : IsMuted ? "Playing (muted)"
        : "Playing";

    /// <summary>Icon kind for the row's play/pause button.</summary>
    public string PlayIcon => IsPlaying ? "Pause" : "Play";

    public string PlayToolTip => IsPlaying ? $"Pause \"{Name}\"" : IsPaused ? $"Resume \"{Name}\"" : $"Play \"{Name}\"";

    /// <summary>Music state of the show (from <see cref="IShowMusicService"/>; None without a music service).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMusic), nameof(HasMusicProblem), nameof(MusicIcon), nameof(MusicText), nameof(MusicToolTip))]
    private ShowMusicState _musicState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MusicToolTip))]
    private string? _musicMessage;

    public bool HasMusic => MusicState != ShowMusicState.None;

    /// <summary>The song is missing or cannot be played: the show runs without music (safety rule 10: shown in the list).</summary>
    public bool HasMusicProblem => MusicState is ShowMusicState.Missing or ShowMusicState.Error;

    public string MusicIcon => HasMusicProblem ? "Warning" : "Music";

    /// <summary>Music column text: empty when fine, a short word when there is a problem.</summary>
    public string MusicText => MusicState switch
    {
        ShowMusicState.Missing => "Missing",
        ShowMusicState.Error => "Error",
        _ => string.Empty,
    };

    public string MusicToolTip => MusicState switch
    {
        ShowMusicState.Loading => "Loading the show's music…",
        ShowMusicState.Ready => "This show plays its music in sync with the lights.",
        ShowMusicState.Missing or ShowMusicState.Error => MusicMessage ?? "The show's music cannot be played.",
        _ => "No music",
    };

    /// <summary>Copies the summary and the item blocks from the project's show.</summary>
    internal void Update(Show show)
    {
        Name = show.Name;
        LengthSeconds = double.IsFinite(show.LengthSeconds) ? Math.Max(0, show.LengthSeconds) : 0;
        Bpm = show.Bpm;
        TrackCount = show.Tracks.Count;
        ItemCount = show.Tracks.Sum(t => t.Items.Count);
        Blocks = BuildBlocks(show, LengthSeconds);
        OnPropertyChanged(nameof(PlayToolTip));
    }

    internal void SetMusic(ShowMusicState state, string? message)
    {
        MusicMessage = message;
        MusicState = state;
    }

    internal void SetState(bool running, bool paused, bool muted)
    {
        IsRunning = running;
        IsPaused = running && paused;
        IsMuted = running && muted;
    }

    private static List<ShowTimelineBlock> BuildBlocks(Show show, double length)
    {
        var blocks = new List<ShowTimelineBlock>();
        var rows = show.Tracks.Count;
        if (length <= 0 || rows == 0)
        {
            return blocks;
        }

        var rowHeight = 1.0 / rows;
        for (var row = 0; row < rows; row++)
        {
            foreach (var item in show.Tracks[row].Items)
            {
                if (!item.HasValidTiming || item.StartSeconds >= length)
                {
                    continue;
                }

                var left = item.StartSeconds / length;
                var width = Math.Min(item.EndSeconds, length) / length - left;
                blocks.Add(new ShowTimelineBlock(left, width, row * rowHeight, rowHeight));
            }
        }

        return blocks;
    }
}
