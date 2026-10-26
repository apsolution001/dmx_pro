using System.Windows;
using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Lets the pulse, flash and strobe editor views open the modal Choose lights dialog themselves (their settings tab
/// has a "Choose lights…" button and Ctrl+L, while the Lights tab with its own <see cref="FixtureListPanel"/> may not be
/// loaded yet). Same view concern as in <see cref="FixtureListPanel"/>: only tells the ViewModel how to show the dialog.
/// </summary>
internal static class EffectEditorPicker
{
    /// <summary>Hooks <paramref name="view"/>'s DataContext (an <see cref="EffectEditorViewModel"/>) up to the dialog.</summary>
    public static void Attach(FrameworkElement view)
    {
        bool Show(FixturePickerViewModel picker) => FixturePickerWindow.ShowModal(picker, Window.GetWindow(view));
        Func<FixturePickerViewModel, bool> show = Show;

        view.DataContextChanged += (_, e) =>
        {
            if (e.OldValue is EffectEditorViewModel old && old.Fixtures.ShowPicker == show)
            {
                old.Fixtures.ShowPicker = null;
            }

            if (e.NewValue is EffectEditorViewModel viewModel)
            {
                viewModel.Fixtures.ShowPicker = show;
            }
        };

        // The Lights tab's FixtureListPanel also sets its own (owner = its window), even while its tab is not shown and it
        // has no window yet. Once the editor is on screen, take over again so the dialog always gets this window as owner.
        view.Loaded += (_, _) =>
        {
            if (view.DataContext is EffectEditorViewModel viewModel)
            {
                viewModel.Fixtures.ShowPicker = show;
            }
        };
    }
}
