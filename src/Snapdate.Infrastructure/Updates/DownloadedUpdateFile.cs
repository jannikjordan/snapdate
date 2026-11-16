using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.Updates;

/// <summary>
/// A verified-by-handle download: one read handle (other readers allowed, writers and deleters denied) held from
/// before verification until the installer process has been started.
/// </summary>
internal sealed class DownloadedUpdateFile : IDownloadedFile
{
    private readonly FileStream _stream;
    private readonly string _folder;
    private bool _keep;
    private bool _disposed;

    public DownloadedUpdateFile(FileStream stream, string path, string folder)
    {
        _stream = stream;
        Path = path;
        _folder = folder;
    }

    public string Path { get; }

    public Stream Content => _stream;

    public void KeepOnDisk() => _keep = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        if (!_keep)
        {
            HttpUpdateDownloader.TryDeleteFolder(_folder);
        }
    }
}
