using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;
using Wpf.Ui.Controls;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Top menu and main toolbar actions that live on other pages: each one goes to the page it belongs to (when that makes
/// sense) and runs that page's own command, so menu, toolbar, context menus and page shortcuts all behave the same.
/// Items are disabled whenever the page has nothing to act on.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly VirtualConsoleViewModel _virtualConsole;
    private readonly TestPageViewModel _testPage;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>Forwarding commands whose target depends on the current page; refreshed on every page switch.</summary>
    private readonly List<ForwardingCommand> _pageCommands = [];

    private bool _lastBlackout;
    private ForwardingCommand? _cutCommand;
    private ForwardingCommand? _copyCommand;
    private ForwardingCommand? _pasteCommand;
    private ForwardingCommand? _duplicateCommand;
    private ForwardingCommand? _deleteCommand;
    private ForwardingCommand? _renameCommand;
    private ForwardingCommand? _autoPatchCommand;
    private ForwardingCommand? _addTrackCommand;
    private ForwardingCommand? _showPlayPauseCommand;
    private ForwardingCommand? _showStopCommand;

    /// <summary>File ▸ Open recent, newest first (from the app settings).</summary>
    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Blackout state for the checked Output ▸ Blackout item (any source: menu, toolbar, console widget).</summary>
    public bool IsBlackout => _virtualConsole.Playback.IsBlackout;

    /// <summary>Operator mode of the Virtual Console (Tools ▸ Operator mode, F12).</summary>
    public bool IsOperatorMode => _virtualConsole.IsOperatorMode;

    // ───────────── Edit (current page) ─────────────

    /// <summary>Edit ▸ Cut (Ctrl+X): functions only — copies the selected function and deletes it (with confirmation).</summary>
    public ICommand CutCommand => _cutCommand ??= PageCommand(() => OnPage(_functions) ? _functions.CutCommand : null, _functions.CutCommand);

    /// <summary>Edit ▸ Copy (Ctrl+C): functions only.</summary>
    public ICommand CopyCommand => _copyCommand ??= PageCommand(() => OnPage(_functions) ? _functions.CopyCommand : null, _functions.CopyCommand);

    /// <summary>Edit ▸ Paste (Ctrl+V): functions only; adds a copy of the copied function to the library.</summary>
    public ICommand PasteCommand => _pasteCommand ??= PageCommand(() => OnPage(_functions) ? _functions.PasteCommand : null, _functions.PasteCommand);

    /// <summary>Edit ▸ Duplicate: fixtures, functions, show timeline items, console widgets (edit layout).</summary>
    public ICommand DuplicateCommand => _duplicateCommand ??= PageCommand(
        () => CurrentPage switch
        {
            FixturesViewModel => _fixtures.DuplicateCommand,
            FunctionsViewModel => _functions.DuplicateCommand,
            ShowsViewModel => _shows.DuplicateSelectedItemCommand,
            VirtualConsoleViewModel => _virtualConsole.DuplicateWidgetCommand,
            _ => null,
        },
        _fixtures.DuplicateCommand,
        _functions.DuplicateCommand,
        _shows.DuplicateSelectedItemCommand,
        _virtualConsole.DuplicateWidgetCommand);

    /// <summary>Edit ▸ Delete: the same items as Duplicate, each with its page's own confirmation.</summary>
    public ICommand DeleteCommand => _deleteCommand ??= PageCommand(
        () => CurrentPage switch
        {
            FixturesViewModel => _fixtures.DeleteCommand,
            FunctionsViewModel => _functions.DeleteCommand,
            ShowsViewModel => _shows.DeleteSelectedItemCommand,
            VirtualConsoleViewModel => _virtualConsole.DeleteWidgetCommand,
            _ => null,
        },
        _fixtures.DeleteCommand,
        _functions.DeleteCommand,
        _shows.DeleteSelectedItemCommand,
        _virtualConsole.DeleteWidgetCommand);

    /// <summary>Edit ▸ Rename: selected function, selected show, or the console page (not in operator mode).</summary>
    public ICommand RenameCommand => _renameCommand ??= PageCommand(
        () => CurrentPage switch
        {
            FunctionsViewModel => _functions.BeginRenameCommand,
            ShowsViewModel => _shows.BeginRenameShowCommand,
            VirtualConsoleViewModel when _virtualConsole.ShowEditChrome => _virtualConsole.RenamePageCommand,
            _ => null,
        },
        _functions.BeginRenameCommand,
        _shows.BeginRenameShowCommand,
        _virtualConsole.RenamePageCommand);

    // ───────────── Fixtures / Show (go to the page, then act) ─────────────

    /// <summary>Fixtures ▸ Auto-patch selected (needs a fixture selection on the Fixtures page).</summary>
    public ICommand AutoPatchCommand => _autoPatchCommand ??= new ForwardingCommand(
        () => _fixtures.AutoPatchCommand, () => GoTo(_fixtures), _fixtures.AutoPatchCommand);

    /// <summary>Show ▸ Add track (to the show selected on the Shows page).</summary>
    public ICommand AddTrackCommand => _addTrackCommand ??= new ForwardingCommand(
        () => _shows.AddTrackCommand, () => GoTo(_shows), _shows.AddTrackCommand);

    /// <summary>Show ▸ Play / Pause of the show selected on the Shows page.</summary>
    public ICommand ShowPlayPauseCommand => _showPlayPauseCommand ??= new ForwardingCommand(
        () => _shows.PlayPauseCommand, () => GoTo(_shows), _shows.PlayPauseCommand);

    /// <summary>Show ▸ Stop &amp; return to start.</summary>
    public ICommand ShowStopCommand => _showStopCommand ??= new ForwardingCommand(
        () => _shows.StopCommand,
        () =>
        {
            GoTo(_shows);

            // A running show stops and goes back to where Play started; this first Stop does that, the forwarded
            // second Stop (show now stopped) returns the playhead to 0.
            _shows.StopCommand.Execute(null);
        },
        _shows.StopCommand);

    /// <summary>Hooks the menu state up to the pages; called once at the end of the constructor.</summary>
    private void InitializeMenu()
    {
        _lastBlackout = _virtualConsole.Playback.IsBlackout;
        _virtualConsole.Playback.StateChanged += OnPlaybackStateChanged;
        _virtualConsole.PropertyChanged += OnVirtualConsolePropertyChanged;
        _functions.StatusMessage += (_, message) => ShowInfo(InfoBarSeverity.Success, message);
        _projects.StateChanged += (_, _) => RememberRecentProject();
        _appSettings.Changed += (_, _) => RebuildRecentProjects();
        RebuildRecentProjects();
    }

    partial void OnSelectedItemChanged(NavigationItem? oldValue, NavigationItem newValue)
    {
        foreach (var command in _pageCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    // ───────────── File ▸ Open recent ─────────────

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Check before asking to save the current project: a missing file should not cost the user a decision.
        if (!File.Exists(path))
        {
            ForgetMissingProject(path);
            return;
        }

        using var pause = _fileSync.Suspend();
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        try
        {
            var issues = await _projects.OpenAsync(path);
            ShowIssues(issues, $"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            ForgetMissingProject(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DmxFileFormatException)
        {
            await _dialogs.ShowErrorAsync("Could not open project", ex.Message);
        }
    }

    private void ForgetMissingProject(string path)
    {
        _appSettings.Update(s => s.RecentProjects = RecentProjectList.Remove(s.RecentProjects, path));
        ShowInfo(
            InfoBarSeverity.Warning,
            $"\"{Path.GetFileName(path)}\" was not found (moved, renamed or deleted), so it was removed from " +
            $"Open recent. It was at {path}");
    }

    /// <summary>After every open and save (the project is clean and has a file), that file moves to the top of the list.</summary>
    private void RememberRecentProject()
    {
        if (_projects.FilePath is not { } path || _projects.IsDirty)
        {
            return;
        }

        var recent = _appSettings.Current.RecentProjects;
        if (recent.Count > 0 && RecentProjectList.SamePath(recent[0], path))
        {
            return;
        }

        _appSettings.Update(s => s.RecentProjects = RecentProjectList.Add(s.RecentProjects, path));
    }

    private void RebuildRecentProjects()
    {
        RecentProjects.Clear();
        var number = 1;
        foreach (var path in _appSettings.Current.RecentProjects)
        {
            RecentProjects.Add(new RecentProjectItem(number++, path, OpenRecentCommand));
        }

        OnPropertyChanged(nameof(HasRecentProjects));
    }

    // ───────────── View / Fixtures / Functions / Show / Tools ─────────────

    /// <summary>View ▸ DMX monitor (Ctrl+M): opens Test &amp; Preview with its DMX monitor panel shown.</summary>
    [RelayCommand]
    private void ShowMonitor()
    {
        GoTo(_testPage);
        _testPage.IsMonitorVisible = true;
    }

    [RelayCommand]
    private void NewGroup()
    {
        GoTo(_fixtures);
        _fixtures.NewGroupCommand.Execute(null);
    }

    /// <summary>Fixtures ▸ New fixture profile: the channel layout editor for a new layout, as on the Add fixture dialog.</summary>
    [RelayCommand]
    private void NewFixtureProfile()
    {
        var editor = new ChannelLayoutViewModel(_profileLibrary, _projects, _dialogs);
        if (_dialogs.ShowChannelLayout(editor) && editor.SavedProfile is { } saved)
        {
            ShowInfo(
                InfoBarSeverity.Success,
                $"Created channel layout \"{saved.DisplayName}\". Patch lights with it in Fixtures ▸ Add fixture (Ctrl+Shift+A).");
        }
    }

    /// <summary>
    /// Tools ▸ Profile editor: on the Fixtures page with a light selected it edits that light's channel layout (Ctrl+L);
    /// anywhere else it starts a new layout.
    /// </summary>
    [RelayCommand]
    private void OpenProfileEditor()
    {
        if (OnPage(_fixtures) && _fixtures.EditChannelLayoutCommand.CanExecute(null))
        {
            _fixtures.EditChannelLayoutCommand.Execute(null);
            return;
        }

        NewFixtureProfile();
    }

    /// <summary>Functions ▸ New scene / chaser / …: opens the Functions page with the new function in its editor.</summary>
    [RelayCommand]
    private void NewFunction(FunctionType type)
    {
        GoTo(_functions);
        _functions.NewFunctionCommand.Execute(type);
    }

    [RelayCommand]
    private void NewShow()
    {
        GoTo(_shows);
        _shows.NewShowCommand.Execute(null);
    }

    /// <summary>Tools ▸ Operator mode (F12): switches operator mode and shows the Virtual Console it applies to.</summary>
    [RelayCommand]
    private void ToggleOperatorMode()
    {
        GoTo(_virtualConsole);
        _virtualConsole.ToggleOperatorModeCommand.Execute(null);
    }

    // ───────────── Live safety actions (no page switch, no message bar: they must not move the console) ─────────────

    /// <summary>
    /// Functions ▸ Stop all functions, Output ▸ Release all (Shift+Esc): ends held flashes, then the engine Release All
    /// stops every function and show and returns channels to their defaults (safety rule 4). Grand Master and Blackout
    /// are left as they are. Asks first only when Settings ▸ "Confirm Release All" is on.
    /// </summary>
    [RelayCommand]
    private async Task ReleaseAllAsync()
    {
        if (_appSettings.Current.ConfirmReleaseAll
            && !await _dialogs.ConfirmAsync(
                "Release all",
                "Stop every function and show and return all channels to their defaults?",
                "Release all"))
        {
            return;
        }

        _virtualConsole.ReleaseFlashes();
        _virtualConsole.Playback.ReleaseAll();
    }

    /// <summary>
    /// Output ▸ Blackout (Ctrl+B): toggles the dark state. Output keeps sending (Blackout ≠ output disabled, safety rule 3).
    /// </summary>
    [RelayCommand]
    private void ToggleBlackout()
    {
        _virtualConsole.Playback.SetBlackout(!_virtualConsole.Playback.IsBlackout);
        RefreshBlackout();
    }

    // ───────────── Helpers ─────────────

    private ForwardingCommand PageCommand(Func<ICommand?> resolve, params ICommand[] watched)
    {
        var command = new ForwardingCommand(resolve, null, watched);
        _pageCommands.Add(command);
        return command;
    }

    private bool OnPage(PageViewModel page) => ReferenceEquals(CurrentPage, page);

    private void GoTo(PageViewModel page)
    {
        if (!OnPage(page))
        {
            SelectedItem = NavigationItems.First(n => ReferenceEquals(n.Page, page));
        }
    }

    /// <summary>Playback state changes for every fader move and may come from any thread: only blackout matters here.</summary>
    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            RefreshBlackout();
        }
        else
        {
            _uiContext.Post(_ => RefreshBlackout(), null);
        }
    }

    private void RefreshBlackout()
    {
        var blackout = _virtualConsole.Playback.IsBlackout;
        if (blackout != _lastBlackout)
        {
            _lastBlackout = blackout;
            OnPropertyChanged(nameof(IsBlackout));
        }
    }

    private void OnVirtualConsolePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VirtualConsoleViewModel.IsOperatorMode))
        {
            OnPropertyChanged(nameof(IsOperatorMode));
            _renameCommand?.NotifyCanExecuteChanged();
        }
    }
}
