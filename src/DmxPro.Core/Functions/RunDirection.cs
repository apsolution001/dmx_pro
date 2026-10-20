namespace DmxPro.Core.Functions;

/// <summary>Which end of the step list a step-based function starts from.</summary>
public enum RunDirection
{
    /// <summary>First step to last step.</summary>
    Forward,

    /// <summary>Last step to first step.</summary>
    Backward,
}
