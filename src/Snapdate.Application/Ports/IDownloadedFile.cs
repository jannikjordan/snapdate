namespace Snapdate.Application.Ports;

/// <summary>
/// A downloaded release file in a private folder. The file stays open for reading with other writers and
/// deleters locked out for as long as this object lives, so what was verified is what gets started.
/// </summary>
public interface IDownloadedFile : IDisposable
{
    /// <summary>Full path of the file.</summary>
    string Path { get; }

    /// <summary>The held read handle, positioned at the start. Do not dispose it.</summary>
    Stream Content { get; }

    /// <summary>After this, <see cref="IDisposable.Dispose"/> only closes the handle and leaves the files (an installer is running from them).</summary>
    void KeepOnDisk();
}
