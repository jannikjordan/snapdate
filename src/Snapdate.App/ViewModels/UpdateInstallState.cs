namespace Snapdate.App.ViewModels;

/// <summary>What the update banner is doing: offering, or one step of the in-app update, or showing a failure.</summary>
public enum UpdateInstallState
{
    /// <summary>The normal "new version available" banner.</summary>
    Idle,

    /// <summary>Downloading the installer (progress bar and Cancel).</summary>
    Downloading,

    /// <summary>Checking the signature of the download.</summary>
    Verifying,

    /// <summary>The installer is running; waiting for it (no time limit, the window stays closable).</summary>
    Installing,

    /// <summary>The update failed; a short message and the release page button are shown.</summary>
    Failed,
}
