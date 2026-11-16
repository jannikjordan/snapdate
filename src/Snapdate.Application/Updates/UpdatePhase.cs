namespace Snapdate.Application.Updates;

/// <summary>Where <see cref="InstallUpdateUseCase"/> currently is; reported for the UI.</summary>
public enum UpdatePhase
{
    /// <summary>Downloading the signature and the installer.</summary>
    Downloading,

    /// <summary>Checking the signature of the downloaded installer.</summary>
    Verifying,

    /// <summary>The installer was started; waiting for it (Windows may show an administrator prompt).</summary>
    Installing,
}
