using Snapdate.Application.Localization;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Settings;

/// <summary>What Snapdate remembers between runs. Stored locally only.</summary>
public sealed record AppSettings
{
    public string? SourceFolder { get; init; }

    public string? TargetFolder { get; init; }

    public string Pattern { get; init; } = NamePattern.DefaultText;

    public bool IncludeSubfolders { get; init; } = true;

    public TransferMode Mode { get; init; } = TransferMode.Copy;

    /// <summary>Explicit language choice; null means "follow Windows".</summary>
    public UiLanguage? Language { get; init; }

    /// <summary>Explicit theme choice ("light"/"dark"); null means "follow Windows".</summary>
    public string? Theme { get; init; }
}
