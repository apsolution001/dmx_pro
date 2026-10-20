namespace DmxPro.App.ViewModels;

/// <summary>A choice in a settings picker (bind <c>SelectedValuePath="Value"</c>, <c>DisplayMemberPath="Label"</c>).</summary>
/// <typeparam name="T">The setting's value type.</typeparam>
/// <param name="Value">Stored value.</param>
/// <param name="Label">Shown text.</param>
/// <param name="Description">Explanation shown under the picker.</param>
public sealed record SettingOption<T>(T Value, string Label, string Description);
