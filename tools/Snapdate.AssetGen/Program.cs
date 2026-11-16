// Snapdate asset generator (developer tool, not shipped).
//
//   dotnet run --project tools/Snapdate.AssetGen -c Release -- icons
//   dotnet run --project tools/Snapdate.AssetGen -c Release -- screenshots
//   dotnet run --project tools/Snapdate.AssetGen -c Release            (both)
//
// icons:       src/Snapdate.App/Assets/app.ico, assets/branding/logo-{256,512}.png
// screenshots: docs/images/{en,de}/*.png rendered headlessly from the real views.

using Avalonia;
using Avalonia.Headless;
using Snapdate.AssetGen;

var repoRoot = RepoRoot.Find();
var what = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

AppBuilder.Configure<Snapdate.App.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

if (what is "icons" or "all")
{
    IconRenderer.Run(repoRoot);
}

if (what is "screenshots" or "all")
{
    ScreenshotRenderer.Run(repoRoot);
}

return 0;
