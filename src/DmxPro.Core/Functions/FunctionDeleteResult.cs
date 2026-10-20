namespace DmxPro.Core.Functions;

/// <summary>What <see cref="FunctionLibrary.DeleteFunction"/> did.</summary>
public enum FunctionDeleteResult
{
    /// <summary>No function with that id exists.</summary>
    NotFound,

    /// <summary>The function was removed from the project.</summary>
    Deleted,

    /// <summary>
    /// The function is a show's own copy that the show still uses: it was only taken off the Functions list
    /// (<see cref="LightFunction.ShowInFunctionList"/> = false) so the show keeps working.
    /// </summary>
    HiddenFromList,
}
