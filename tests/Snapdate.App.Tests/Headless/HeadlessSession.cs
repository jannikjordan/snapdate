using Avalonia;
using Avalonia.Headless;

[assembly: AssemblyFixture(typeof(Snapdate.App.Tests.Headless.HeadlessSession))]

namespace Snapdate.App.Tests.Headless;

/// <summary>
/// One headless Avalonia UI thread shared by all UI tests in this assembly.
/// Deliberately not disposed: disposing the session blocks on shutdown of the
/// dispatcher; the background UI thread ends with the test process instead.
/// </summary>
public sealed class HeadlessSession
{
    public HeadlessSession()
    {
        Session = HeadlessUnitTestSession.StartNew(typeof(HeadlessSession));
    }

    public HeadlessUnitTestSession Session { get; }

    /// <summary>Entry point used by <see cref="HeadlessUnitTestSession.StartNew(Type)"/>.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
