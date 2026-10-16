namespace DmxPro.Core.Serialization;

/// <summary>A project or profile file is unreadable, corrupt, or from a newer, unsupported version.</summary>
public sealed class DmxFileFormatException : Exception
{
    public DmxFileFormatException(string message)
        : base(message)
    {
    }

    public DmxFileFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
