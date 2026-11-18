using System.Globalization;
using System.Text.Json;
using Snapdate.Application.FileNames;
using Snapdate.Application.Ports;
using Snapdate.Domain.FileNames;
using Snapdate.Infrastructure.IO;

namespace Snapdate.Infrastructure.FileNames;

/// <summary>
/// Built-in rules from the JSON embedded in this assembly; the user's rules from <c>%APPDATA%\Snapdate\date-rules.json</c>
/// (ADR 0007). The user file is untrusted: it is size-capped, invalid entries are dropped, and a broken file is reported
/// as <see cref="UserRulesStatus.Unreadable"/> and moved aside before the next save. Never throws.
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

    private readonly IClock? _clock = clock;

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
    public DateRuleSaveResult SaveUserRules(UserDateRules rules)
    {
        if (rules is null)
        {
            return DateRuleSaveResult.Failed;
        }

        lock (Gate)
        {
            var current = Read().Status;
            switch (current)
            {
                case UserRulesStatus.NewerVersion:
                    // A newer version's data is never lost.
                    return DateRuleSaveResult.ReadOnly;
                case UserRulesStatus.Inaccessible:
                    // The rules on disk are unknown; writing now could replace rules the user still has.
                    return DateRuleSaveResult.Failed;
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(ToFile(rules), DateRulesJsonContext.Default.UserDateRulesFile);
            if (bytes.Length > MaxFileSize)
            {
                // It would be unreadable on the next start.
                return DateRuleSaveResult.TooLarge;
            }

            if (current == UserRulesStatus.Unreadable && !TryMoveAside())
            {
                return DateRuleSaveResult.Failed;
            }

            return AtomicFile.TryWrite(filePath, bytes) ? DateRuleSaveResult.Saved : DateRuleSaveResult.Failed;
        }
    }

    private UserDateRulesLoad Read()
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return UserDateRulesLoad.Missing;
            }

            if (info.Length > MaxFileSize)
            {
                return Unreadable();
            }

            bytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Not "unreadable": the content may be fine (e.g. locked for a moment) and must not be replaced.
            return new UserDateRulesLoad(UserDateRules.Empty, UserRulesStatus.Inaccessible);
        }

        // Editors such as Notepad may save UTF-8 with a byte order mark, which the JSON reader does not skip.
        var json = bytes.AsMemory(bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0);
        if (json.Length > MaxFileSize || ReadSchemaVersion(json) is not { } version || version < 1)
        {
            return Unreadable();
        }

        var file = Deserialize(json.Span);
        if (version > SchemaVersion)
        {
            // Best effort: a newer file may have a shape this version cannot read; it is used if possible and never saved.
            return new UserDateRulesLoad(file is null ? UserDateRules.Empty : ToRules(file), UserRulesStatus.NewerVersion);
        }

        return file is null ? Unreadable() : new UserDateRulesLoad(ToRules(file), UserRulesStatus.Ok);

        static UserDateRulesLoad Unreadable() => new(UserDateRules.Empty, UserRulesStatus.Unreadable);
    }

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
