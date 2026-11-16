using Avalonia.Controls.ApplicationLifetimes;

namespace Snapdate.App.Services;

/// <summary><see cref="IAppExit"/> for the classic desktop lifetime.</summary>
public sealed class AvaloniaAppExit(IClassicDesktopStyleApplicationLifetime lifetime) : IAppExit
{
    public void CloseApp() => lifetime.Shutdown();
}
