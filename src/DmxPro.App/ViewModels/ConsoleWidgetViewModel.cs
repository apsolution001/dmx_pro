using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One widget on a console page. Shows the widget's live state (from <see cref="Services.IConsolePlayback"/>) and
/// edits its properties in edit mode. Layout (X/Y/size) is changed only through the owning
/// <see cref="VirtualConsoleViewModel"/>, which goes through the project's snapping/clamping API.
/// </summary>
public sealed partial class ConsoleWidgetViewModel : ObservableObject
{
    private readonly VirtualConsoleViewModel _console;
    private double _level;
    private bool _flashHeld;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(AutomationName))]
    private bool _isActive;

    [ObservableProperty]
    private bool _hasColorError;

    public ConsoleWidgetViewModel(VirtualConsoleViewModel console, VirtualConsolePageViewModel page, ConsoleWidget model)
    {
        _console = console;
        Page = page;
        Model = model;
        Refresh();
    }

    public ConsoleWidget Model { get; }

    public VirtualConsolePageViewModel Page { get; }

    public Guid Id => Model.Id;

    public VirtualConsoleWidgetKind Kind => Model switch
    {
        FunctionWidget { Mode: FunctionWidgetMode.Flash } => VirtualConsoleWidgetKind.FlashButton,
        FunctionWidget => VirtualConsoleWidgetKind.FunctionButton,
        ShowWidget => VirtualConsoleWidgetKind.ShowButton,
        GroupLevelWidget => VirtualConsoleWidgetKind.GroupLevel,
        MasterWidget => VirtualConsoleWidgetKind.Master,
        BlackoutWidget => VirtualConsoleWidgetKind.Blackout,
        ReleaseAllWidget => VirtualConsoleWidgetKind.ReleaseAll,
        SpeedWidget => VirtualConsoleWidgetKind.Speed,
        _ => VirtualConsoleWidgetKind.Label,
    };

    public WidgetPresentation Presentation => Page.Model.PresentationOf(Model);

    public int X => Model.X;

    public int Y => Model.Y;

    public int Width => Model.Width;

    public int Height => Model.Height;

    public string Label
    {
        get => Model.Label;
        set
        {
            var text = value ?? string.Empty;
            if (text == Model.Label)
            {
                return;
            }

            Model.Label = text;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(AutomationName));
            _console.OnWidgetEdited();
        }
    }

    /// <summary>The text on the widget: its label, else the name of what it controls, else its kind.</summary>
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Model.Label) ? Model.Label : TargetName ?? VirtualConsoleViewModel.DefaultLabel(Kind);

    /// <summary>The widget colour as "#RRGGBB", or null for the theme colour.</summary>
    public string? Color => Model.Color;

    public bool HasColor => !string.IsNullOrEmpty(Model.Color);

    /// <summary>Colour as typed in the properties panel; empty = theme colour. Invalid text is not stored.</summary>
    public string ColorText
    {
        get => Model.Color ?? string.Empty;
        set
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                SetColor(null);
                return;
            }

            if (!text.StartsWith('#'))
            {
                text = "#" + text;
            }

            if (text.Length == 7 && int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                SetColor(text.ToUpperInvariant());
            }
            else
            {
                HasColorError = true;
            }
        }
    }

    public bool HasMode => Model is FunctionWidget;

    public FunctionWidgetMode Mode
    {
        get => (Model as FunctionWidget)?.Mode ?? FunctionWidgetMode.Toggle;
        set
        {
            if (Model is not FunctionWidget function || function.Mode == value)
            {
                return;
            }

            ReleaseFlash();
            var oldDefault = VirtualConsoleViewModel.DefaultLabel(Kind);
            function.Mode = value;
            if (Model.Label == oldDefault)
            {
                Model.Label = VirtualConsoleViewModel.DefaultLabel(Kind);
                OnPropertyChanged(nameof(Label));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(Kind));
            Refresh();
            _console.OnWidgetEdited();
        }
    }

    public string KeyShortcut
    {
        get => Model.KeyShortcut ?? string.Empty;
        set
        {
            var text = (value ?? string.Empty).Trim().ToUpperInvariant();
            var stored = text.Length == 0 ? null : text;
            if (stored == Model.KeyShortcut)
            {
                return;
            }

            ReleaseFlash();
            Model.KeyShortcut = stored;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToolTipText));
            _console.OnWidgetEdited();
        }
    }

    /// <summary>True for widgets that control a function, show or group (shows the target picker).</summary>
    public bool HasTarget => Model is FunctionWidget or ShowWidget or GroupLevelWidget or SpeedWidget;

    /// <summary>Caption of the target picker.</summary>
    public string TargetCaption => Model switch
    {
        ShowWidget => "Show",
        GroupLevelWidget => "Group",
        _ => "Function",
    };

    /// <summary>Id of the controlled function/show/group; <see cref="Guid.Empty"/> = none (or all functions for Speed).</summary>
    public Guid TargetId
    {
        get => Model switch
        {
            FunctionWidget f => f.FunctionId,
            ShowWidget s => s.ShowId,
            GroupLevelWidget g => g.GroupId,
            SpeedWidget s => s.FunctionId ?? Guid.Empty,
            _ => Guid.Empty,
        };
        set
        {
            if (!HasTarget || value == TargetId)
            {
                return;
            }

            ReleaseFlash();
            var oldName = TargetName;
            var followsTarget = string.IsNullOrWhiteSpace(Model.Label)
                || Model.Label == oldName
                || Model.Label == VirtualConsoleViewModel.DefaultLabel(Kind);
            switch (Model)
            {
                case FunctionWidget f:
                    f.FunctionId = value;
                    break;
                case ShowWidget s:
                    s.ShowId = value;
                    break;
                case GroupLevelWidget g:
                    g.GroupId = value;
                    break;
                case SpeedWidget s:
                    s.FunctionId = value == Guid.Empty ? null : value;
                    break;
            }

            if (followsTarget && TargetName is { } newName)
            {
                Model.Label = newName;
                OnPropertyChanged(nameof(Label));
            }

            OnPropertyChanged();
            Refresh();
            _console.OnWidgetEdited();
        }
    }

    /// <summary>Name of the controlled function/show/group, or null when none is set or it no longer exists.</summary>
    public string? TargetName => HasTarget ? _console.FindTargetName(Model, TargetId) : null;

    /// <summary>A function/show/group widget whose target is not set or was deleted: it does nothing when pressed.</summary>
    public bool IsUnassigned => HasTarget && TargetName is null && (Model is not SpeedWidget || TargetId != Guid.Empty);

    /// <summary>Icon kind (see Assets/Icons/README.md) or null for none.</summary>
    public string? Icon => IsUnassigned ? "Warning" : Kind switch
    {
        VirtualConsoleWidgetKind.FlashButton => "Flash",
        VirtualConsoleWidgetKind.ShowButton => IsActive ? "Stop" : "Play",
        VirtualConsoleWidgetKind.Blackout => "Blackout",
        VirtualConsoleWidgetKind.ReleaseAll => "ReleaseAll",
        VirtualConsoleWidgetKind.Master => "Dimmer",
        VirtualConsoleWidgetKind.GroupLevel => "Group",
        VirtualConsoleWidgetKind.Speed => "Pulse",
        _ => null,
    };

    public bool HasIcon => Icon is not null;

    /// <summary>State in words, so the active state never depends on colour alone.</summary>
    public string StateText
    {
        get
        {
            if (IsUnassigned)
            {
                return TargetId == Guid.Empty ? "Not assigned" : "Missing";
            }

            if (!IsActive)
            {
                return string.Empty;
            }

            return Kind switch
            {
                VirtualConsoleWidgetKind.ShowButton => "RUNNING",
                VirtualConsoleWidgetKind.FlashButton => "FLASH",
                _ => "ON",
            };
        }
    }

    public bool HasStateText => StateText.Length > 0;

    /// <summary>Fader position 0..1. Setting it sends the level to playback (ignored in edit mode).</summary>
    public double Level
    {
        get => _level;
        set
        {
            var level = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (_console.IsEditMode || IsUnassigned || !SetProperty(ref _level, level))
            {
                OnPropertyChanged();
                return;
            }

            ApplyLevel(level);
            OnPropertyChanged(nameof(ValueText));
        }
    }

    public string ValueText => Model is SpeedWidget
        ? string.Create(CultureInfo.CurrentCulture, $"{SpeedFactor(_level):0.00}×")
        : string.Create(CultureInfo.CurrentCulture, $"{Math.Round(_level * 100):0}%");

    public string ToolTipText
    {
        get
        {
            var what = Kind switch
            {
                VirtualConsoleWidgetKind.FunctionButton => Presentation == WidgetPresentation.Slider
                    ? "Fader: sets the function level"
                    : "Click to switch the function on/off",
                VirtualConsoleWidgetKind.FlashButton => Presentation == WidgetPresentation.Slider
                    ? "Fader: sets the function level"
                    : "Hold to flash the function",
                VirtualConsoleWidgetKind.ShowButton => "Click to start/stop the show",
                VirtualConsoleWidgetKind.GroupLevel => "Group level",
                VirtualConsoleWidgetKind.Master => "Grand master",
                VirtualConsoleWidgetKind.Blackout => "Blackout on/off (output keeps sending a dark frame)",
                VirtualConsoleWidgetKind.ReleaseAll => "Stop all functions and shows",
                VirtualConsoleWidgetKind.Speed => Model is SpeedWidget { FunctionId: null }
                    ? "Speed of all functions"
                    : "Speed of the function",
                _ => "Label",
            };
            return string.IsNullOrEmpty(Model.KeyShortcut) ? what : $"{what} (key {Model.KeyShortcut})";
        }
    }

    public string AutomationName => HasStateText ? $"{DisplayLabel}, {StateText}" : DisplayLabel;

    /// <summary>Speed fader position 0..1 → factor 0.25..4 (centre = 1×).</summary>
    public static double SpeedFactor(double level) => Math.Pow(2, (level - 0.5) * 4);

    /// <summary>Click on a toggle-type tile (function toggle, show, blackout, release all).</summary>
    [RelayCommand]
    public void Activate()
    {
        if (_console.IsEditMode || IsUnassigned)
        {
            return;
        }

        var playback = _console.Playback;
        switch (Model)
        {
            case FunctionWidget { Mode: FunctionWidgetMode.Toggle } f:
                playback.ToggleFunction(f.FunctionId);
                break;
            case ShowWidget s:
                playback.ToggleShow(s.ShowId);
                break;
            case BlackoutWidget:
                playback.SetBlackout(!playback.IsBlackout);
                break;
            case ReleaseAllWidget:
                _console.ReleaseFlashes();
                playback.ReleaseAll();
                break;
        }
    }

    /// <summary>Pointer/key down on a flash tile.</summary>
    [RelayCommand]
    public void Press()
    {
        if (_console.IsEditMode || IsUnassigned || _flashHeld || Kind != VirtualConsoleWidgetKind.FlashButton
            || Presentation != WidgetPresentation.Button)
        {
            return;
        }

        _flashHeld = true;
        _console.OnFlashHeld(this, true);
        _console.Playback.FlashDown(((FunctionWidget)Model).FunctionId);
    }

    /// <summary>Pointer/key up, lost capture or deactivation: ends a held flash (safety rule 5).</summary>
    [RelayCommand]
    public void Release() => ReleaseFlash();

    internal void ReleaseFlash()
    {
        if (!_flashHeld)
        {
            return;
        }

        _flashHeld = false;
        _console.OnFlashHeld(this, false);
        if (Model is FunctionWidget f)
        {
            _console.Playback.FlashUp(f.FunctionId);
        }
    }

    /// <summary>Re-reads live state and target names.</summary>
    internal void Refresh()
    {
        IsActive = ReadIsActive();
        _level = ReadLevel();
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(TargetName));
        OnPropertyChanged(nameof(IsUnassigned));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasStateText));
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(ToolTipText));
    }

    /// <summary>
    /// Playback state changed (raised for every fader move, flash, toggle): re-reads only the live state and notifies only
    /// what changed. Names and targets come from the project and are refreshed by <see cref="Refresh"/>.
    /// </summary>
    internal void RefreshState()
    {
        var active = ReadIsActive();
        if (active != IsActive)
        {
            IsActive = active; // also raises StateText and AutomationName
            OnPropertyChanged(nameof(HasStateText));
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(HasIcon));
        }

        var level = ReadLevel();
        if (level != _level)
        {
            _level = level;
            OnPropertyChanged(nameof(Level));
            OnPropertyChanged(nameof(ValueText));
        }
    }

    /// <summary>Position, size or presentation changed.</summary>
    internal void RefreshLayout()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Presentation));
        OnPropertyChanged(nameof(ToolTipText));
    }

    private bool ReadIsActive()
    {
        var playback = _console.Playback;
        return Model switch
        {
            FunctionWidget f => playback.IsFunctionActive(f.FunctionId),
            ShowWidget s => playback.IsShowRunning(s.ShowId),
            BlackoutWidget => playback.IsBlackout,
            _ => false,
        };
    }

    private double ReadLevel()
    {
        var playback = _console.Playback;
        return Model switch
        {
            FunctionWidget f => playback.GetFunctionLevel(f.FunctionId),
            GroupLevelWidget g => playback.GetGroupLevel(g.GroupId),
            MasterWidget => playback.GrandMaster,
            SpeedWidget s => Math.Clamp((Math.Log2(playback.GetSpeed(s.FunctionId)) / 4) + 0.5, 0, 1),
            _ => 0,
        };
    }

    private void SetColor(string? color)
    {
        HasColorError = false;
        if (color == Model.Color)
        {
            OnPropertyChanged(nameof(ColorText));
            return;
        }

        Model.Color = color;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(ColorText));
        _console.OnWidgetEdited();
    }

    internal void ApplyColorPreset(string? hex) => SetColor(hex);

    private void ApplyLevel(double level)
    {
        var playback = _console.Playback;
        switch (Model)
        {
            case FunctionWidget f:
                playback.SetFunctionLevel(f.FunctionId, level);
                break;
            case GroupLevelWidget g:
                playback.SetGroupLevel(g.GroupId, level);
                break;
            case MasterWidget:
                playback.SetGrandMaster(level);
                break;
            case SpeedWidget s:
                playback.SetSpeed(s.FunctionId, SpeedFactor(level));
                break;
        }
    }
}
