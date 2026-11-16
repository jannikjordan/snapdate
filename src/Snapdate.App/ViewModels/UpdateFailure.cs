namespace Snapdate.App.ViewModels;

/// <summary>Why the in-app update failed (the text is built from this, so it follows the language).</summary>
public enum UpdateFailure
{
    None,
    Download,
    Verification,
    MissingInstaller,
    Start,
    Installer,
}
