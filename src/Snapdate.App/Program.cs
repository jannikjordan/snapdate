using Avalonia;

namespace Snapdate.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [SelfTest.Argument])
        {
            return SelfTest.Run();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by the visual designer and the self-test.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
