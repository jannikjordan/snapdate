using Snapdate.Application.Localization;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Settings;

/// <summary>What Snapdate remembers between runs. Stored locally only.</summary>
// Properties use "set" (not "init") on purpose: the source-generated JSON deserializer drops the
// property defaults of init-only members when a key is missing from an older settings file.
public sealed record AppSettings
{
    public string? SourceFolder { get; set; }

    public string? TargetFolder { get; set; }

    public string Pattern { get; set; } = NamePattern.DefaultText;

    public bool IncludeSubfolders { get; set; } = true;

    public TransferMode Mode { get; set; } = TransferMode.Copy;

    /// <summary>Explicit language choice; null means "follow Windows".</summary>
    public UiLanguage? Language { get; set; }

    /// <summary>Explicit theme choice ("light"/"dark"); null means "follow Windows".</summary>
    public string? Theme { get; set; }

    /// <summary>Check for new versions on start (at most once a day). On by default; the user can switch it off.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>When the release feed last answered (UTC); limits automatic checks to one per day.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>Version the user chose not to be notified about (SemVer text), or null.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>True once the one-time "Snapdate checks GitHub once a day" notice has been shown.</summary>
    public bool UpdateNoticeShown { get; set; }
}
