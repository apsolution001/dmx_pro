namespace DmxPro.App.ViewModels;

/// <summary>An entry of the Settings category list.</summary>
/// <param name="Section">The category.</param>
/// <param name="Title">Label.</param>
/// <param name="IconKind">Icon name from the app icon set (<c>Icon.&lt;IconKind&gt;</c>).</param>
public sealed record SettingsSectionItem(SettingsSection Section, string Title, string IconKind);
