namespace Snapdate.Application.Ports;

/// <summary>A release file could not be downloaded safely (network, policy, size, disk).</summary>
public sealed class UpdateDownloadException : Exception
{
    public UpdateDownloadException()
    {
    }

    public UpdateDownloadException(string message)
        : base(message)
    {
    }

    public UpdateDownloadException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
