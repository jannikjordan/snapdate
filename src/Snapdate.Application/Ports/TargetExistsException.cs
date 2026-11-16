namespace Snapdate.Application.Ports;

/// <summary>The target file already exists; nothing was overwritten.</summary>
public sealed class TargetExistsException : IOException
{
    public TargetExistsException()
    {
    }

    public TargetExistsException(string message)
        : base(message)
    {
    }

    public TargetExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
