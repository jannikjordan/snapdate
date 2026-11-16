using Snapdate.Domain.Versioning;

namespace Snapdate.Application.Updates;

/// <summary>A published release as reported by the release feed. Untrusted until verified.</summary>
public sealed record ReleaseInfo(
    AppVersion Version,
    Uri PageUrl,
    bool IsDraft,
    bool IsPreRelease,
    IReadOnlyList<ReleaseAsset> Assets);
