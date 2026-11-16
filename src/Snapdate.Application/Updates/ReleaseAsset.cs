namespace Snapdate.Application.Updates;

/// <summary>A downloadable file attached to a release (e.g. installer, portable exe, <c>.sig</c>).</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);
