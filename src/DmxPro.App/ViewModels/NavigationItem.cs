using Wpf.Ui.Controls;

namespace DmxPro.App.ViewModels;

/// <summary>An entry in the left navigation rail.</summary>
public sealed record NavigationItem(string Title, SymbolRegular Icon, PageViewModel Page);
