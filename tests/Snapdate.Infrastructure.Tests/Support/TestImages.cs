using System.Buffers.Binary;
using System.Text;

namespace Snapdate.Infrastructure.Tests.Support;

/// <summary>
/// Builds tiny, valid image files with known metadata in memory, so tests need no binary fixtures.
/// </summary>
internal static class TestImages
{
    /// <summary>A JPEG with an EXIF APP1 segment (IFD0 Model + Exif SubIFD DateTimeOriginal).</summary>
    public static byte[] JpegWithExif(DateTime? taken, string? model)
    {
        var tiff = BuildTiff(taken, model);
        var app1 = new List<byte> { 0xFF, 0xE1 };
        var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        var length = (ushort)(payload.Length + 2);
        app1.Add((byte)(length >> 8));
        app1.Add((byte)length);
        app1.AddRange(payload);

        return [0xFF, 0xD8, .. app1, 0xFF, 0xD9];
    }

    /// <summary>A 1×1 PNG with a tEXt "Creation Time" chunk.</summary>
    public static byte[] PngWithCreationTime(string? creationTime)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), 1);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type RGB
        png.AddRange(Chunk("IHDR", ihdr));
        if (creationTime is not null)
        {
            png.AddRange(Chunk("tEXt", Encoding.Latin1.GetBytes("Creation Time\0" + creationTime)));
        }

        png.AddRange(Chunk("IEND", []));
        return [.. png];
    }

    private static byte[] BuildTiff(DateTime? taken, string? model)
    {
        // Little-endian TIFF: header, IFD0 (Model, ExifIFD pointer), Exif IFD (DateTimeOriginal), data.
        var entries0 = new List<(ushort Tag, ushort Type, uint Count, byte[] Data)>();
        if (model is not null)
        {
            entries0.Add((0x0110, 2, (uint)(model.Length + 1), Encoding.ASCII.GetBytes(model + "\0")));
        }

        var hasExif = taken is not null;
        if (hasExif)
        {
            entries0.Add((0x8769, 4, 1, [])); // pointer filled below
        }

        const int headerSize = 8;
        var ifd0Size = 2 + (12 * entries0.Count) + 4;
        var modelOffset = headerSize + ifd0Size;
        var modelSize = model is null ? 0 : Even(model.Length + 1);
        var exifIfdOffset = modelOffset + modelSize;
        const int exifIfdSize = 2 + 12 + 4;
        var dateOffset = exifIfdOffset + exifIfdSize;

        var buffer = new byte[dateOffset + (hasExif ? 20 : 0)];
        "II"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), headerSize);

        var p = headerSize;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p), (ushort)entries0.Count);
        p += 2;
        foreach (var (tag, type, count, data) in entries0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(p + 4), count);
            var value = tag == 0x8769 ? (uint)exifIfdOffset : (uint)modelOffset;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(p + 8), value);
            if (tag == 0x0110)
            {
                data.CopyTo(buffer, modelOffset);
            }

            p += 12;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(p), 0); // no next IFD

        if (hasExif)
        {
            p = exifIfdOffset;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p + 2), 0x9003); // DateTimeOriginal
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p + 4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(p + 6), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(p + 10), (uint)dateOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(p + 14), 0);
            Encoding.ASCII.GetBytes(taken!.Value.ToString("yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\0")
                .CopyTo(buffer, dateOffset);
        }

        return buffer;
    }

    private static int Even(int n) => n % 2 == 0 ? n : n + 1;

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32([.. typeBytes, .. data]));
        return chunk;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
