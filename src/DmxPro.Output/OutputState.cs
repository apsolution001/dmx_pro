namespace DmxPro.Output;

/// <summary>Coarse state of an output, shown in the status-bar pill.</summary>
public enum OutputState
{
    /// <summary>Not transmitting. The state on every app start and project open (safety rule 1).</summary>
    Disabled,

    /// <summary>Started; opening the socket / device.</summary>
    Connecting,

    /// <summary>Transport is open and frames are being transmitted.</summary>
    Sending,

    /// <summary>Hardware or network problem; <see cref="OutputStatus.Message"/> explains it. The output keeps retrying.</summary>
    Error,
}
