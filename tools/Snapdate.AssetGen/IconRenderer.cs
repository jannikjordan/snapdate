using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Snapdate.App.Controls;

namespace Snapdate.AssetGen;

/// <summary>Renders the BrandLogo control to PNGs and a multi-size Windows .ico.</summary>
internal static class IconRenderer
{
    private static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    public static void Run(string repoRoot)
    {
        var icoPath = Path.Combine(repoRoot, "src", "Snapdate.App", "Assets", "app.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        WriteIco(icoPath, IcoSizes.Select(size => (size, RenderPng(size))).ToList());
        Console.WriteLine($"wrote {Path.GetRelativePath(repoRoot, icoPath)}");

        foreach (var size in new[] { 256, 512 })
        {
            var path = Path.Combine(repoRoot, "assets", "branding", $"logo-{size}.png");
            File.WriteAllBytes(path, RenderPng(size));
            Console.WriteLine($"wrote {Path.GetRelativePath(repoRoot, path)}");
        }
    }

    private static byte[] RenderPng(int size)
    {
        // The control must live in a shown window so styles, resources and layout apply.
        var logo = new BrandLogo { Width = size, Height = size, IsSimplified = size <= 24 };
        var host = new Window
        {
            Content = logo,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.Transparent,
        };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(logo);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        host.Close();
        return stream.ToArray();
    }

    // ICO container with PNG-compressed entries (supported since Windows Vista).
    private static void WriteIco(string path, List<(int Size, byte[] Png)> images)
    {
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: icon
        writer.Write((ushort)images.Count);

        var offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // width (0 = 256)
            writer.Write((byte)(size >= 256 ? 0 : size)); // height
            writer.Write((byte)0);                         // palette
            writer.Write((byte)0);                         // reserved
            writer.Write((ushort)1);                       // colour planes
            writer.Write((ushort)32);                      // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }
    }
}
