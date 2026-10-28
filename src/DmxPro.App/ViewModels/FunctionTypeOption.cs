using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels;

/// <summary>An entry of the Functions page "Type" filter or the Shows "new function" type list; <see cref="Type"/> null = all types.</summary>
public sealed record FunctionTypeOption(FunctionType? Type, string Name)
{
    /// <summary>
    /// The function types the app creates and edits (owner decision 2026-10-28): scenes and chasers only. Older types
    /// (colour cycle, fade, pulse, flash, strobe, sequence) are converted when a project loads and are never offered.
    /// </summary>
    public static IReadOnlyList<FunctionType> CreatableTypes { get; } = [FunctionType.Scene, FunctionType.Chaser];

    /// <summary>True for a type in <see cref="CreatableTypes"/>.</summary>
    public static bool IsCreatable(FunctionType type) => type is FunctionType.Scene or FunctionType.Chaser;

    /// <summary>One option per <see cref="CreatableTypes"/> entry, in that order.</summary>
    public static IReadOnlyList<FunctionTypeOption> Creatable() =>
        CreatableTypes.Select(t => new FunctionTypeOption(t, FunctionRowViewModel.TypeDisplayName(t))).ToList();

    public override string ToString() => Name;
}
