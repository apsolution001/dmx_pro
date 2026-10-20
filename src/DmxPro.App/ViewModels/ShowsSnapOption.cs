using DmxPro.Core.Shows;

namespace DmxPro.App.ViewModels;

/// <summary>One entry of the Shows page Snap picker.</summary>
public sealed record ShowsSnapOption(TimelineSnapMode Mode, string Label);
