using System.Text;
using Celbridge.Browser.Helpers;
using Celbridge.WebHost;
using SkiaSharp;

namespace Celbridge.Tests.Browser;

[TestFixture]
public class FaviconEncoderTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // A 1 by 1 GIF. Skia has no GIF encoder, so the test contains the file as base64 text.
    private const string GifBase64 = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    private static void ShouldBeAPng(byte[]? png, (int Width, int Height) size)
    {
        png.Should().NotBeNull();
        png!.Take(PngSignature.Length).Should().Equal(PngSignature);
        TestFavicons.SizeOf(png).Should().Be(size);
    }

    [Test]
    public void Encode_ALargeIcon_IsScaledToFitWithItsProportions()
    {
        var png = FaviconEncoder.Encode(TestFavicons.Png(256, 128));

        ShouldBeAPng(png, (32, 16));
    }

    [Test]
    public void Encode_ASmallIcon_KeepsItsSize()
    {
        var png = FaviconEncoder.Encode(TestFavicons.Png(16, 16));

        ShouldBeAPng(png, (16, 16));
    }

    [Test]
    public void Encode_AScaledIcon_KeepsItsColor()
    {
        var png = FaviconEncoder.Encode(TestFavicons.Png(128, 128, SKColors.Red));

        TestFavicons.CenterColor(png!).Should().Be(SKColors.Red);
    }

    [Test]
    public void Encode_AnIcoFile_GivesAPng()
    {
        var ico = TestFavicons.Ico(TestFavicons.Png(48, 48), 48);

        var png = FaviconEncoder.Encode(ico);

        ShouldBeAPng(png, (32, 32));
    }

    [TestCase(SKEncodedImageFormat.Jpeg)]
    [TestCase(SKEncodedImageFormat.Webp)]
    public void Encode_AnotherFormat_GivesAPng(SKEncodedImageFormat format)
    {
        var png = FaviconEncoder.Encode(TestFavicons.Encode(64, 64, format));

        ShouldBeAPng(png, (32, 32));
    }

    [Test]
    public void Encode_AGifFile_GivesAPng()
    {
        var png = FaviconEncoder.Encode(Convert.FromBase64String(GifBase64));

        ShouldBeAPng(png, (1, 1));
    }

    [Test]
    public void Encode_ABmpFile_GivesAPng()
    {
        var png = FaviconEncoder.Encode(TestFavicons.Bmp(2, 3));

        ShouldBeAPng(png, (2, 3));
    }

    [Test]
    public void Encode_AnIconAtTheSourceLimit_IsScaled()
    {
        var png = FaviconEncoder.Encode(TestFavicons.Png(FaviconEncoder.MaximumSourcePixels, 32));

        ShouldBeAPng(png, (32, 1));
    }

    [Test]
    public void Encode_AnImageOverTheSourceLimit_IsNull()
    {
        var wide = TestFavicons.Png(FaviconEncoder.MaximumSourcePixels + 1, 1);
        var tall = TestFavicons.Png(1, FaviconEncoder.MaximumSourcePixels + 1);

        FaviconEncoder.Encode(wide).Should().BeNull();
        FaviconEncoder.Encode(tall).Should().BeNull();
    }

    [Test]
    public void Encode_BytesThatAreNotAnImage_IsNull()
    {
        var svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        FaviconEncoder.Encode(Array.Empty<byte>()).Should().BeNull();
        FaviconEncoder.Encode(new byte[] { 1, 2, 3 }).Should().BeNull();
        FaviconEncoder.Encode(PngSignature).Should().BeNull();
        FaviconEncoder.Encode(svg).Should().BeNull();
    }

    [Test]
    public void Encode_APngCutShort_IsNull()
    {
        FaviconEncoder.Encode(TestFavicons.CutShortPng()).Should().BeNull();
    }

    [Test]
    public void CanDecode_AValidPng_IsTrue()
    {
        FaviconEncoder.CanDecode(TestFavicons.Png(16, 16)).Should().BeTrue();
    }

    [Test]
    public void CanDecode_APngCutShortWithAValidSignature_IsFalse()
    {
        var cutShort = TestFavicons.CutShortPng();

        // SkiaSharp still reads the header. Only the decode of the image data fails.
        using var data = SKData.CreateCopy(cutShort);
        using var codec = SKCodec.Create(data);
        codec.Should().NotBeNull();

        BrowserBookmark.IsStorableFavicon(cutShort).Should().BeTrue();
        FaviconEncoder.CanDecode(cutShort).Should().BeFalse();
    }

    [Test]
    public void CanDecode_BytesThatAreNotAnImage_IsFalse()
    {
        FaviconEncoder.CanDecode(Array.Empty<byte>()).Should().BeFalse();
        FaviconEncoder.CanDecode(PngSignature).Should().BeFalse();
    }
}
