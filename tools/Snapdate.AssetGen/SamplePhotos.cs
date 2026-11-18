using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Snapdate.AssetGen;

/// <summary>
/// Creates a believable demo photo folder: rendered landscape "photos" saved as JPEG with real EXIF
/// capture dates, plus files without metadata (screenshot, WhatsApp pictures, video) to show every badge.
/// A WhatsApp picture with a contradicting date written by an app shows the "Dates differ" hint.
/// </summary>
internal static class SamplePhotos
{
    /// <param name="Name">File name.</param>
    /// <param name="Taken">EXIF DateTimeOriginal; null for no EXIF.</param>
    /// <param name="Camera">EXIF camera model; null with a <paramref name="Taken"/> date means "written by an app".</param>
    /// <param name="Hue">Colour of the rendered picture.</param>
    /// <param name="FileDate">Created and modified time of the file.</param>
    private sealed record Sample(string Name, DateTime? Taken, string? Camera, int Hue, DateTime FileDate);

    private static readonly Sample[] Samples =
    [
        new("IMG_4711.JPG", new DateTime(2024, 7, 14, 9, 12, 33), "iPhone 15 Pro", 205, new DateTime(2024, 8, 2)),
        new("IMG_4712.JPG", new DateTime(2024, 7, 14, 9, 14, 2), "iPhone 15 Pro", 190, new DateTime(2024, 8, 2)),
        new("DSC00012.JPG", new DateTime(2024, 7, 14, 16, 1, 55), "ILCE-7M4", 28, new DateTime(2024, 8, 3)),
        new("DSC00013.JPG", new DateTime(2024, 7, 14, 16, 1, 55), "ILCE-7M4", 16, new DateTime(2024, 8, 3)),
        new("IMG_4730.JPG", new DateTime(2024, 7, 15, 7, 45, 10), "iPhone 15 Pro", 160, new DateTime(2024, 8, 2)),
        new("WhatsApp Image 2024-07-15 at 19.05.22.jpeg", null, null, 300, new DateTime(2024, 8, 4, 10, 12, 0)),
        new("Screenshot 2024-07-15.png", null, null, 260, new DateTime(2024, 7, 15, 21, 4, 40)),
        new("IMG_4741.JPG", new DateTime(2024, 7, 16, 12, 30, 0), "iPhone 15 Pro", 45, new DateTime(2024, 8, 2)),
        new("IMG-20240716-WA0003.jpg", null, null, 330, new DateTime(2024, 7, 16, 19, 5, 22)),
        new("IMG-20240716-WA0007.jpg", new DateTime(2024, 8, 4, 10, 30, 12), null, 80, new DateTime(2024, 8, 4, 10, 30, 12)),
        new("DSC00031.JPG", new DateTime(2024, 7, 17, 18, 44, 31), "ILCE-7M4", 12, new DateTime(2024, 8, 3)),
        new("MVI_0021.MP4", null, null, 280, new DateTime(2024, 7, 17, 11, 20, 0)),
        new("IMG_4790.JPG", new DateTime(2024, 7, 18, 10, 2, 47), "iPhone 15 Pro", 140, new DateTime(2024, 8, 2)),
        new("IMG_4791.JPG", new DateTime(2024, 7, 18, 10, 3, 5), "iPhone 15 Pro", 100, new DateTime(2024, 8, 2)),
    ];

    public static void Create(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var s in Samples)
        {
            var path = System.IO.Path.Combine(folder, s.Name);
            var ext = System.IO.Path.GetExtension(s.Name).ToLowerInvariant();
            byte[] bytes = ext switch
            {
                ".png" => Render(s.Hue, png: true),
                ".mp4" => [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2'],
                _ => InjectExif(Render(s.Hue, png: false), s.Taken, s.Camera),
            };
            File.WriteAllBytes(path, bytes);
            File.SetCreationTime(path, s.FileDate);
            File.SetLastWriteTime(path, s.FileDate);
        }
    }

    private static byte[] Render(int hue, bool png)
    {
        const int w = 240, h = 180;
        var sky = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Hsl(hue, 0.65, 0.72), 0), new GradientStop(Hsl(hue + 30, 0.6, 0.86), 1) },
        };
        var canvas = new Canvas { Width = w, Height = h, Background = sky };
        var sun = new Ellipse { Width = 46, Height = 46, Fill = new SolidColorBrush(Hsl(hue + 180, 0.9, 0.7)) };
        Canvas.SetLeft(sun, 30 + (hue % 120));
        Canvas.SetTop(sun, 26);
        canvas.Children.Add(sun);
        canvas.Children.Add(Mountain("M0,180 L0,120 L60,70 L110,130 L150,95 L240,150 L240,180 Z", Hsl(hue + 200, 0.35, 0.42)));
        canvas.Children.Add(Mountain("M0,180 L0,150 L80,115 L150,150 L200,125 L240,140 L240,180 Z", Hsl(hue + 210, 0.4, 0.28)));

        var host = new Window { Content = canvas, SizeToContent = SizeToContent.WidthAndHeight };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        bitmap.Render(canvas);
        host.Close();

        using var stream = new MemoryStream();
        bitmap.Save(stream, png ? PngBitmapEncoderOptions.Default : new JpegBitmapEncoderOptions { Quality = 88 });
        return stream.ToArray();
    }

    private static Avalonia.Controls.Shapes.Path Mountain(string data, Color color) =>
        new() { Data = Geometry.Parse(data), Fill = new SolidColorBrush(color) };

    private static Color Hsl(int hue, double s, double l) =>
        HslColor.ToRgb((hue % 360 + 360) % 360, s, l, 1);

    /// <summary>
    /// Inserts an EXIF APP1 segment (Model, if any, + DateTimeOriginal) right after the JPEG SOI marker.
    /// Without a model the date looks like one written by an app, not a camera.
    /// </summary>
    private static byte[] InjectExif(byte[] jpeg, DateTime? taken, string? model)
    {
        if (taken is null)
        {
            return jpeg;
        }

        var modelBytes = model is null ? [] : Encoding.ASCII.GetBytes(model + "\0");
        var modelSize = modelBytes.Length + (modelBytes.Length % 2);
        var entries = model is null ? 1 : 2;
        var ifd0Size = 2 + (entries * 12) + 4;
        var modelOffset = 8 + ifd0Size;
        var exifIfdOffset = modelOffset + modelSize;
        var dateOffset = exifIfdOffset + 2 + 12 + 4;
        var tiff = new byte[dateOffset + 20];

        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), (ushort)entries);
        var entry = 10;
        if (model is not null)
        {
            WriteEntry(tiff, entry, 0x0110, 2, (uint)modelBytes.Length, (uint)modelOffset); // Model
            entry += 12;
        }

        WriteEntry(tiff, entry, 0x8769, 4, 1, (uint)exifIfdOffset);                       // Exif IFD pointer
        modelBytes.CopyTo(tiff, modelOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(exifIfdOffset), 1);
        WriteEntry(tiff, exifIfdOffset + 2, 0x9003, 2, 20, (uint)dateOffset);          // DateTimeOriginal
        Encoding.ASCII.GetBytes(taken.Value.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture) + "\0").CopyTo(tiff, dateOffset);

        var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static void WriteEntry(byte[] buffer, int at, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at + 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at + 4), count);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at + 8), value);
    }
}
