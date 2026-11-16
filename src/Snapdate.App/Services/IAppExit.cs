namespace Snapdate.App.Services;

/// <summary>Ends the application (abstracted so tests can observe it).</summary>
public interface IAppExit
{
    /// <summary>Closes the app now. Used only after a completed in-app update (the new version is already running).</summary>
    void CloseApp();
}
