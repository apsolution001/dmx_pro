using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels;

/// <summary>An entry of the Functions page "Type" filter; <see cref="Type"/> null = all types.</summary>
public sealed record FunctionTypeOption(FunctionType? Type, string Name)
{
    public override string ToString() => Name;
}
