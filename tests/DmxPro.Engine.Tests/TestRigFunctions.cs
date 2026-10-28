using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>Function helpers for <see cref="TestRig"/>.</summary>
internal static class TestRigFunctions
{
    /// <summary>Adds <paramref name="function"/> to the rig's project and returns it.</summary>
    public static T Add<T>(this TestRig rig, T function)
        where T : LightFunction
    {
        rig.Project.Functions.Add(function);
        return function;
    }
}
