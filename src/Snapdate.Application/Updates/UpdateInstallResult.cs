namespace Snapdate.Application.Updates;

/// <summary>Outcome of <see cref="InstallUpdateUseCase"/>. Nothing here ever means "partly installed".</summary>
public enum UpdateInstallResult
{
    /// <summary>
    /// The verified installer ran and exited with code 0 while Snapdate was still running (normally Setup closes
    /// Snapdate itself, so most successful updates never get to see this result).
    /// </summary>
    Completed,

    /// <summary>
    /// The app is shutting down (the shutdown token was signalled, typically because Setup's Restart Manager closes
    /// Snapdate) while the installer is still running. The outcome is not known any more; nothing was cleaned up.
    /// </summary>
    InstallerRunning,

    /// <summary>
    /// The silent path does not apply: not installed, install not confirmed by the registry, release not newer,
    /// draft or pre-release. The UI should fall back to the release page.
    /// </summary>
    NotApplicable,

    /// <summary>The release has no (valid) installer or <c>.sig</c> for this PC's architecture. No other architecture is tried.</summary>
    AssetMissing,

    /// <summary>The download failed (network, size mismatch, disk). Nothing was run; files were removed.</summary>
    DownloadFailed,

    /// <summary>The signature did not match. The file was deleted and never run.</summary>
    VerificationFailed,

    /// <summary>The verified installer process could not be started at all.</summary>
    LaunchFailed,

    /// <summary>
    /// The installer started but exited with a non-zero code: for example the user declined the administrator
    /// prompt of an all-users update, or Setup refused to downgrade. Nothing was installed; files were removed.
    /// </summary>
    InstallerFailed,

    /// <summary>The caller cancelled during download or verification; downloaded files were removed.</summary>
    Cancelled,
}
