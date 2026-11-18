using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Snapdate.Application.FileNames;
using Snapdate.Application.Ports;
using Snapdate.Domain.FileNames;
using Snapdate.Infrastructure.IO;

namespace Snapdate.Infrastructure.FileNames;

/// <summary>
/// Built-in rules from the JSON embedded in this assembly; the user's rules from <c>%APPDATA%\Snapdate\date-rules.json</c>
/// (ADR 0007). The user file is untrusted: it is size-capped, invalid entries are dropped, and a broken file is reported
/// as <see cref="UserRulesStatus.Unreadable"/> and moved aside before the next save. A save is refused when the file
/// changed since it was loaded (<see cref="UserRulesFingerprint"/>). Never throws.
/// </summary>
/// <param name="filePath">Path of the user's rule file.</param>
/// <param name="clock">Clock for the time stamp of a moved-aside file; default the system clock.</param>
public sealed class JsonDateRuleStore(string filePath, IClock? clock = null) : IDateRuleStore
{
    /// <summary>Upper bound for the user file; anything larger is unreadable (and never written).</summary>
    public const long MaxFileSize = 64 * 1024;

    /// <summary>The schema version this build reads and writes.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Manifest name of the embedded built-in rules.</summary>
    internal const string SystemRulesResourceName = "Snapdate.Infrastructure.FileNames.system-date-rules.json";

    // Further unreadable files with the same time stamp get "-2", "-3", ... appended; give up after this many.
    private const int MaxAsideAttempts = 100;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly Lazy<IReadOnlyList<DateRule>> SystemRules = new(ReadSystemRules);

    // One lock for all instances: the rules dialog and a scan may load while another save runs.
    private static readonly Lock Gate = new();

    // Bytes of an oversized file that go into its fingerprint; it is never hashed in full (it may be gigabytes).
    private const int OversizedFingerprintPrefix = 64 * 1024;

    private readonly IClock? _clock = clock;

    /// <summary>Writes the user file; replaced in tests to simulate a failing disk.</summary>
    internal Func<string, byte[], bool> Writer { get; init; } = AtomicFile.TryWrite;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapdate", "date-rules.json");

    /// <inheritdoc />
    public IReadOnlyList<DateRule> LoadSystemRules() => SystemRules.Value;

    /// <inheritdoc />
    public UserDateRulesLoad LoadUserRules()
    {
        lock (Gate)
        {
            return Read();
        }
    }

    /// <inheritdoc />
    public UserDateRulesSave SaveUserRules(UserDateRules rules, UserRulesFingerprint expected)
    {
        if (rules is null || expected is null)
        {
            return new UserDateRulesSave(DateRuleSaveResult.Failed, expected ?? UserRulesFingerprint.Unknown);
        }

        lock (Gate)
        {
            var current = Read();
            if (current.Status == UserRulesStatus.Inaccessible)
            {
                // The rules on disk are unknown; writing now could replace rules the user still has.
                return Refused(DateRuleSaveResult.Failed);
            }

            if (!expected.Matches(current.Fingerprint))
            {
                // Another instance saved, or the file was edited or repaired since it was loaded: never overwrite that.
                return Refused(DateRuleSaveResult.Conflict);
            }

            if (current.Status == UserRulesStatus.NewerVersion)
            {
                // A newer version's data is never lost.
                return Refused(DateRuleSaveResult.ReadOnly);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(ToFile(rules), DateRulesJsonContext.Default.UserDateRulesFile);
            if (bytes.Length > MaxFileSize)
            {
                // It would be unreadable on the next start.
                return Refused(DateRuleSaveResult.TooLarge);
            }

            // Still the same unreadable bytes as at load: keep them aside instead of overwriting them.
            var movedAside = false;
            if (current.Status == UserRulesStatus.Unreadable)
            {
                if (!TryMoveAside())
                {
                    return Refused(DateRuleSaveResult.Failed);
                }

                movedAside = true;
            }

            if (Writer(filePath, bytes))
            {
                return new UserDateRulesSave(DateRuleSaveResult.Saved, FingerprintOf(bytes));
            }

            // The unreadable file is gone now: a retry must be based on "no file", or it would be a conflict forever.
            return movedAside
                ? new UserDateRulesSave(DateRuleSaveResult.Failed, UserRulesFingerprint.Absent)
                : Refused(DateRuleSaveResult.Failed);
        }

        UserDateRulesSave Refused(DateRuleSaveResult result) => new(result, expected);
    }

    private UserDateRulesLoad Read()
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(filePath))
            {
                return UserDateRulesLoad.Missing;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileSize)
            {
                return Unreadable(FingerprintOfOversized(stream, File.GetLastWriteTimeUtc(filePath)));
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted between the check and the open.
            return UserDateRulesLoad.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Not "unreadable": the content may be fine (e.g. locked for a moment) and must not be replaced.
            return new UserDateRulesLoad(UserDateRules.Empty, UserRulesStatus.Inaccessible, UserRulesFingerprint.Unknown);
        }

        // The fingerprint covers the raw bytes, byte order mark included.
        var fingerprint = FingerprintOf(bytes);

        // Editors such as Notepad may save UTF-8 with a byte order mark, which the JSON reader does not skip.
        var json = bytes.AsMemory(bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0);
        if (ReadSchemaVersion(json) is not { } version || version < 1)
        {
            return Unreadable(fingerprint);
        }

        var file = Deserialize(json.Span);
        if (version > SchemaVersion)
        {
            // Best effort: a newer file may have a shape this version cannot read; it is used if possible and never saved.
            return new UserDateRulesLoad(file is null ? UserDateRules.Empty : ToRules(file), UserRulesStatus.NewerVersion, fingerprint);
        }

        return file is null ? Unreadable(fingerprint) : new UserDateRulesLoad(ToRules(file), UserRulesStatus.Ok, fingerprint);

        static UserDateRulesLoad Unreadable(UserRulesFingerprint fingerprint) =>
            new(UserDateRules.Empty, UserRulesStatus.Unreadable, fingerprint);
    }

    /// <summary>
    /// Length, last write time and the first 64 KB: never reads a huge file in full, yet still notices when it is
    /// replaced or repaired.
    /// </summary>
    private static UserRulesFingerprint FingerprintOfOversized(FileStream stream, DateTime lastWriteUtc)
    {
        var prefix = new byte[OversizedFingerprintPrefix];
        var read = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(lastWriteUtc.Ticks));
        hash.AppendData(prefix, 0, read);
        return UserRulesFingerprint.ForContent(stream.Length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static UserRulesFingerprint FingerprintOf(byte[] content) =>
        UserRulesFingerprint.ForContent(content.Length, Convert.ToHexString(SHA256.HashData(content)));

    /// <summary>The schema version; 1 when absent; null when the root is not an object or the version is not an integer.</summary>
    private static int? ReadSchemaVersion(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("schemaVersion", out var version))
            {
                return SchemaVersion;
            }

            return version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UserDateRulesFile? Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, DateRulesJsonContext.Default.UserDateRulesFile);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static UserDateRules ToRules(UserDateRulesFile file)
    {
        var disabled = (file.DisabledSystemRules ?? [])
            .Where(UserDateRules.IsValidId)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var custom = (file.CustomRules ?? [])
            .Where(entry => entry is not null)
            .Select(entry => new DateRule(
                entry!.Id ?? string.Empty,
                entry.Name ?? string.Empty,
                RuleOrigin.Custom,
                entry.Enabled ?? true,
                [.. (entry.Patterns ?? []).Select(pattern => pattern!)],
                entry.Syntax ?? DateRuleSyntax.Pattern))
            .Where(rule => UserDateRules.IsValidRule(rule) && ids.Add(rule.Id))
            .Take(UserDateRules.MaxCustomRules)
            .ToList();

        return new UserDateRules(disabled, custom);
    }

    private static UserDateRulesFile ToFile(UserDateRules rules) => new()
    {
        SchemaVersion = SchemaVersion,
        DisabledSystemRules = [.. (rules.DisabledSystemRuleIds ?? []).Where(UserDateRules.IsValidId).Distinct(StringComparer.Ordinal)],
        CustomRules = [.. (rules.CustomRules ?? [])
            .Where(UserDateRules.IsValidRule)
            .DistinctBy(rule => rule.Id, StringComparer.Ordinal)
            .Take(UserDateRules.MaxCustomRules)
            .Select(rule => new CustomDateRuleEntry
            {
                Id = rule.Id,
                Name = rule.Name,
                Enabled = rule.Enabled,
                Syntax = rule.Syntax,
                Patterns = [.. rule.Patterns],
            })],
    };

    /// <summary>Renames the unreadable file to <c>date-rules.unreadable-YYYYMMDD-HHMMSS[-n].json</c>; never overwrites.</summary>
    private bool TryMoveAside()
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        var stamp = (_clock?.UtcNow ?? DateTimeOffset.UtcNow).ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"{Path.GetFileNameWithoutExtension(filePath)}.unreadable-{stamp}";
        var extension = Path.GetExtension(filePath);

        for (var attempt = 1; attempt <= MaxAsideAttempts; attempt++)
        {
            var target = Path.Combine(folder, attempt == 1 ? baseName + extension : $"{baseName}-{attempt}{extension}");
            if (File.Exists(target))
            {
                continue;
            }

            try
            {
                File.Move(filePath, target, overwrite: false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!File.Exists(filePath))
                {
                    // Someone else moved or deleted it meanwhile; nothing left to protect.
                    return true;
                }

                if (!File.Exists(target))
                {
                    // Not a name clash (e.g. the file is locked): stop instead of overwriting it later.
                    return false;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<DateRule> ReadSystemRules()
    {
        try
        {
            using var stream = typeof(JsonDateRuleStore).Assembly.GetManifestResourceStream(SystemRulesResourceName);
            if (stream is null)
            {
                return [];
            }

            var file = JsonSerializer.Deserialize(stream, DateRulesJsonContext.Default.SystemDateRulesFile);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            return [.. (file?.Rules ?? [])
                .Where(entry => entry is not null)
                .Select(entry => new DateRule(
                    entry!.Id ?? string.Empty,
                    entry.Name ?? string.Empty,
                    RuleOrigin.System,
                    Enabled: true,
                    [.. (entry.Patterns ?? []).Select(pattern => pattern!)],
                    entry.Syntax ?? DateRuleSyntax.Pattern))
                .Where(rule => UserDateRules.IsValidRule(rule) && ids.Add(rule.Id))];
        }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException or InvalidOperationException)
        {
            return [];
        }
    }
}
