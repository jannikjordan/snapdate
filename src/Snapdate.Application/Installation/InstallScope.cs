namespace Snapdate.Application.Installation;

/// <summary>For whom an installed copy was installed.</summary>
public enum InstallScope
{
    /// <summary>Not installed (portable).</summary>
    None,

    /// <summary>Only for the current user (no administrator rights needed to install or update).</summary>
    CurrentUser,

    /// <summary>For all users of the PC (installing or updating needs administrator rights).</summary>
    AllUsers,
}
