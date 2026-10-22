using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Undo / Redo of project edits (Edit menu, main toolbar, Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z outside text boxes). Undo puts
/// earlier content back in place (<see cref="Services.ProjectService.ContentRestored"/>): output, live mode, Grand
/// Master, Blackout and running functions are untouched; a function that no longer exists stops.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>"Undo: Delete fixture "Par 1" (Ctrl+Z)".</summary>
    public string UndoToolTip => _projects.UndoDescription is { } description
        ? $"Undo: {description} (Ctrl+Z)"
        : _projects.CanUndo ? "Undo the last change (Ctrl+Z)" : "Nothing to undo (Ctrl+Z)";

    /// <summary>"Redo: Delete fixture "Par 1" (Ctrl+Y)".</summary>
    public string RedoToolTip => _projects.RedoDescription is { } description
        ? $"Redo: {description} (Ctrl+Y)"
        : "Nothing to redo (Ctrl+Y)";

    private void InitializeHistory()
    {
        _projects.HistoryChanged += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(UndoToolTip));
            OnPropertyChanged(nameof(RedoToolTip));
        };
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!_projects.TryUndo(out var description))
        {
            ShowInfo(InfoBarSeverity.Error, "Undo failed; the project was not changed. Details are in the log.");
        }
        else if (description is not null)
        {
            ShowInfo(InfoBarSeverity.Success, $"Undone: {description}");
        }
    }

    private bool CanUndo() => _projects.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!_projects.TryRedo(out var description))
        {
            ShowInfo(InfoBarSeverity.Error, "Redo failed; the project was not changed. Details are in the log.");
        }
        else if (description is not null)
        {
            ShowInfo(InfoBarSeverity.Success, $"Redone: {description}");
        }
    }

    private bool CanRedo() => _projects.CanRedo;
}
