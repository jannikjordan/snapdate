namespace Snapdate.Application.FileNames;

/// <summary>
/// What the user's rule file looked like when it was read: absent, or its length and SHA-256. A save is refused when
/// the file on disk no longer matches, so an out-of-date catalog (a second app instance, a file repaired by hand while
/// the app runs) never overwrites newer rules.
/// </summary>
public sealed record UserRulesFingerprint
{
    private UserRulesFingerprint(bool isKnown, bool exists, long length, string sha256)
    {
        IsKnown = isKnown;
        Exists = exists;
        Length = length;
        Sha256 = sha256;
    }

    /// <summary>There was no file.</summary>
    public static UserRulesFingerprint Absent { get; } = new(isKnown: true, exists: false, 0, string.Empty);

    /// <summary>The file could not be read (or the rules did not come from a file); no file on disk ever matches it.</summary>
    public static UserRulesFingerprint Unknown { get; } = new(isKnown: false, exists: false, 0, string.Empty);

    /// <summary>False for <see cref="Unknown"/>.</summary>
    public bool IsKnown { get; }

    /// <summary>True when there was a file.</summary>
    public bool Exists { get; }

    /// <summary>File length in bytes; 0 when there was no file.</summary>
    public long Length { get; }

    /// <summary>SHA-256 of the file content as upper-case hex; empty when there was no file.</summary>
    public string Sha256 { get; }

    /// <summary>A file of <paramref name="length"/> bytes with the given SHA-256 (hex, any case).</summary>
    public static UserRulesFingerprint ForContent(long length, string sha256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        return new(isKnown: true, exists: true, length, sha256.ToUpperInvariant());
    }

    /// <summary>True when a file that looks like <paramref name="onDisk"/> is the one this fingerprint was taken of.</summary>
    public bool Matches(UserRulesFingerprint? onDisk) => IsKnown && onDisk is { IsKnown: true } && Equals(onDisk);
}
