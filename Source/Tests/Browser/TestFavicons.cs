using SkiaSharp;

namespace Celbridge.Tests.Browser;

/// <summary>
/// Real images for the favicon tests, drawn and read back with SkiaSharp.
/// </summary>
internal static class TestFavicons
{
    /// <summary>
    /// Returns a PNG of the given size, filled with one color.
    /// </summary>
    public static byte[] Png(int width, int height, SKColor? color = null)
    {
        return Encode(width, height, SKEncodedImageFormat.Png, color);
    }

    /// <summary>
    /// Returns an image of the given size and format, filled with one color.
    /// </summary>
    public static byte[] Encode(int width, int height, SKEncodedImageFormat format, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? SKColors.SteelBlue);
        using var data = bitmap.Encode(format, 100);

        return data.ToArray();
    }

    /// <summary>
    /// Returns an ICO file containing one PNG image. Current icon tools write ICO files this way.
    /// </summary>
    public static byte[] Ico(byte[] png, int size)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // The header: a reserved field, the type for an icon, and the image count.
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);

        // The one directory entry. The header and the entry take 22 bytes, so the image starts there.
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)png.Length);
        writer.Write((uint)22);

        writer.Write(png);
        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>
    /// Returns a 24-bit BMP file of the given size, filled with one color.
    /// </summary>
    public static byte[] Bmp(int width, int height)
    {
        // Each row of pixels pads to a multiple of four bytes.
        var rowLength = (width * 3 + 3) / 4 * 4;
        var pixelLength = rowLength * height;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write((uint)(54 + pixelLength));
        writer.Write((uint)0);
        writer.Write((uint)54);

        writer.Write((uint)40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write((uint)0);
        writer.Write((uint)pixelLength);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write((uint)0);
        writer.Write((uint)0);

        writer.Write(Enumerable.Repeat((byte)0x80, pixelLength).ToArray());
        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>
    /// Returns the width and height of an image.
    /// </summary>
    public static (int Width, int Height) SizeOf(byte[] image)
    {
        using var bitmap = SKBitmap.Decode(image);

        return (bitmap.Width, bitmap.Height);
    }

    /// <summary>
    /// Returns the color at the middle of an image.
    /// </summary>
    public static SKColor CenterColor(byte[] image)
    {
        using var bitmap = SKBitmap.Decode(image);

        return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }
}
