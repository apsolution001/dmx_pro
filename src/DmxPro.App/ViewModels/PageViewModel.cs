using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>Base for every page shown in the main content area.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    public abstract string Subtitle { get; }

    /// <summary>Called each time the page becomes visible.</summary>
    public virtual void OnNavigatedTo()
    {
    }
}
