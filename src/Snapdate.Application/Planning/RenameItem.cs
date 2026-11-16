using Snapdate.Application.Scanning;

namespace Snapdate.Application.Planning;

/// <summary>One planned copy/move: where a photo goes and under which name.</summary>
/// <param name="Photo">The source file.</param>
/// <param name="TargetRelativePath">Relative target path with extension, <c>/</c>-separated.</param>
/// <param name="WasRenamed">True when " (2)" etc. had to be added to avoid a duplicate.</param>
public sealed record RenameItem(ScannedPhoto Photo, string TargetRelativePath, bool WasRenamed)
{
    /// <summary>The new file name only, e.g. <c>2024-07-14_15-32-08.jpg</c>.</summary>
    public string TargetFileName => TargetRelativePath[(TargetRelativePath.LastIndexOf('/') + 1)..];
}
