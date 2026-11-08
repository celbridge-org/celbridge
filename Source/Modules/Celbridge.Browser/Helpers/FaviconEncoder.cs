using SkiaSharp;

namespace Celbridge.Browser.Helpers;

/// <summary>
/// Turns a page's favicon into a PNG for a bookmark to store in the document's file. The image is decoded and scaled
/// to fit MaximumPixels on each side, and a smaller image keeps its size. The PNG contains only the decoded pixels, so
/// none of the page's own bytes reach the file.
/// </summary>
public static class FaviconEncoder
{
    /// <summary>
    /// The largest width and height of a stored icon.
    /// </summary>
    public const int MaximumPixels = 32;

    /// <summary>
    /// The largest width and height of an image the encoder decodes. A small file can declare a huge image. Decoding
    /// allocates memory for every pixel.
    /// </summary>
    public const int MaximumSourcePixels = 1024;

    /// <summary>
    /// Returns the image as a PNG that fits MaximumPixels. Returns null when SkiaSharp fails to read the bytes as an
    /// image. Also returns null for an image wider or taller than MaximumSourcePixels. SkiaSharp chooses the frame to
    /// decode in a file with several frames, like an ICO file.
    /// </summary>
    public static byte[]? Encode(byte[] image)
    {
        if (image.Length == 0)
        {
            return null;
        }

        // SKBitmap.Decode throws for bytes that every codec rejects. SKCodec.Create returns null for those bytes, so the
        // codec comes first. The codec also reads the image's size from its header, and the size check runs before the
        // decode.
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        if (codec is null
            || codec.Info.Width > MaximumSourcePixels
            || codec.Info.Height > MaximumSourcePixels)
        {
            return null;
        }

        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null
            || bitmap.Width == 0
            || bitmap.Height == 0)
        {
            return null;
        }

        var largestSide = Math.Max(bitmap.Width, bitmap.Height);
        if (largestSide <= MaximumPixels)
        {
            return EncodePng(bitmap);
        }

        var scale = (double)MaximumPixels / largestSide;
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        // The mipmaps keep a large downscale smooth, where a single linear pass would skip most of the pixels.
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var scaled = bitmap.Resize(bitmap.Info.WithSize(width, height), sampling);
        if (scaled is null)
        {
            return null;
        }

        return EncodePng(scaled);
    }

    private static byte[]? EncodePng(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        return data?.ToArray();
    }
}
