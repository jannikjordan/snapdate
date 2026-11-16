namespace Snapdate.Domain.Versioning;

/// <summary>
/// A Semantic Versioning 2.0 version (<c>MAJOR.MINOR.PATCH[-prerelease][+build]</c>) with SemVer precedence.
/// A leading "v" is accepted. Build metadata is validated but ignored for equality and ordering.
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly string[] _preRelease;

    private AppVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The pre-release tag without the leading dash (e.g. "rc.1"), or null for a stable version.</summary>
    public string? PreRelease => _preRelease.Length == 0 ? null : string.Join('.', _preRelease);

    public bool IsPreRelease => _preRelease.Length > 0;

    /// <summary>Parses a version; returns false for anything that is not valid SemVer (never throws).</summary>
    public static bool TryParse(string? text, out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64)
        {
            return false;
        }

        var s = text.Trim();
        if (s[0] is 'v' or 'V')
        {
            s = s[1..];
        }

        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            if (!AreValidIdentifiers(s[(plus + 1)..], numericNoLeadingZeros: false))
            {
                return false;
            }

            s = s[..plus];
        }

        string[] pre = [];
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            var tag = s[(dash + 1)..];
            if (!AreValidIdentifiers(tag, numericNoLeadingZeros: true))
            {
                return false;
            }

            pre = tag.Split('.');
            s = s[..dash];
        }

        var parts = s.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch, pre);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var v) ? v! : throw new FormatException($"'{text}' is not a valid version.");

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        // A version without a pre-release tag is higher than one with it.
        if (_preRelease.Length == 0 || other._preRelease.Length == 0)
        {
            return other._preRelease.Length.CompareTo(_preRelease.Length);
        }

        var count = Math.Min(_preRelease.Length, other._preRelease.Length);
        for (var i = 0; i < count; i++)
        {
            c = CompareIdentifier(_preRelease[i], other._preRelease[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is AppVersion v && Equals(v);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    /// <summary>Canonical text without "v" prefix, e.g. "0.2.0-rc.1".</summary>
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPreRelease ? "-" + PreRelease : string.Empty);

    public static bool operator ==(AppVersion? left, AppVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(AppVersion? left, AppVersion? right) => !(left == right);

    public static bool operator <(AppVersion? left, AppVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator >(AppVersion? left, AppVersion? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator <=(AppVersion? left, AppVersion? right) => !(left > right);

    public static bool operator >=(AppVersion? left, AppVersion? right) => !(left < right);

    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = IsNumeric(a);
        var bNumeric = IsNumeric(b);
        if (aNumeric && bNumeric)
        {
            // No leading zeros, so length then ordinal equals numeric order (and cannot overflow).
            var c = a.Length.CompareTo(b.Length);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        if (aNumeric)
        {
            return -1;
        }

        return bNumeric ? 1 : string.CompareOrdinal(a, b);
    }

    private static bool IsNumeric(string s) => s.All(char.IsAsciiDigit);

    private static bool TryParseNumber(string s, out int value)
    {
        value = 0;
        if (s.Length == 0 || s.Length > 9 || !IsNumeric(s) || (s.Length > 1 && s[0] == '0'))
        {
            return false;
        }

        value = int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private static bool AreValidIdentifiers(string text, bool numericNoLeadingZeros)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var id in text.Split('.'))
        {
            if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }

            if (numericNoLeadingZeros && IsNumeric(id) && id.Length > 1 && id[0] == '0')
            {
                return false;
            }
        }

        return true;
    }
}
