using System.Globalization;
using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The colour normalisation must bring every icon into a legible, similar-prominence band while keeping
/// it recognisable, so these assert that a too-dark colour is lifted, a too-bright colour is lowered, the
/// hue is preserved, and a colour already in band is left alone. The band bounds here mirror the ones in
/// the helper.
/// </summary>
[TestFixture]
public class IconColorLegibilityTests
{
    // Loose envelope around the helper's dark-theme band; a normalised colour must land inside it.
    private const double DarkBandFloor = 0.34;
    private const double DarkBandCeiling = 0.56;

    [TestCase("#02303A", TestName = "near-black teal (gradle)")]
    [TestCase("#00599C", TestName = "C++ blue")]
    [TestCase("#217346", TestName = "Excel green")]
    public void DarkColour_OnDark_IsLiftedIntoTheBand(string colorHex)
    {
        var result = IconColorLegibility.Normalize(colorHex, darkBackground: true);

        var luminance = RelativeLuminance(result);
        luminance.Should().BeGreaterThan(RelativeLuminance(colorHex));
        luminance.Should().BeInRange(DarkBandFloor, DarkBandCeiling);
    }

    [Test]
    public void BrightColour_OnDark_IsLoweredIntoTheBand()
    {
        // The bright JavaScript yellow draws the eye more than its neighbours and must be brought down.
        var result = IconColorLegibility.Normalize("#F1E05A", darkBackground: true);

        var luminance = RelativeLuminance(result);
        luminance.Should().BeLessThan(RelativeLuminance("#F1E05A"));
        luminance.Should().BeInRange(DarkBandFloor, DarkBandCeiling);
    }

    [Test]
    public void InBandColour_IsUnchanged()
    {
        // A mid grey already sits inside the dark-theme band, so it is returned as-is.
        IconColorLegibility.Normalize("#A9A9A9", darkBackground: true).Should().Be("#A9A9A9");
    }

    [Test]
    public void Normalisation_PreservesHue()
    {
        // The lifted colour must still read as blue, not drift to another hue.
        var result = IconColorLegibility.Normalize("#00599C", darkBackground: true);

        Hue(result).Should().BeApproximately(Hue("#00599C"), 3.0);
    }

    [Test]
    public void Normalisation_PreservesAlpha()
    {
        var result = IconColorLegibility.Normalize("#8002303A", darkBackground: true);

        result.Should().StartWith("#80");
        result.Length.Should().Be(9);
    }

    [Test]
    public void Normalisation_IsIdempotent()
    {
        var once = IconColorLegibility.Normalize("#00599C", darkBackground: true);
        var twice = IconColorLegibility.Normalize(once, darkBackground: true);

        twice.Should().Be(once);
    }

    [Test]
    public void LightTheme_LowersBrightColoursForAWhiteBackground()
    {
        // On the light theme a bright yellow is illegible on white and must be darkened.
        var result = IconColorLegibility.Normalize("#F1E05A", darkBackground: false);

        RelativeLuminance(result).Should().BeLessThan(RelativeLuminance("#F1E05A"));
    }

    [TestCase("not-a-colour")]
    [TestCase("#12")]
    [TestCase("")]
    public void MalformedColour_IsReturnedUnchanged(string colorHex)
    {
        IconColorLegibility.Normalize(colorHex, darkBackground: true).Should().Be(colorHex);
    }

    private static double RelativeLuminance(string colorHex)
    {
        var (red, green, blue) = Rgb(colorHex);

        double Channel(byte c)
        {
            var value = c / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(red) + 0.7152 * Channel(green) + 0.0722 * Channel(blue);
    }

    private static double Hue(string colorHex)
    {
        var (redByte, greenByte, blueByte) = Rgb(colorHex);
        var r = redByte / 255.0;
        var g = greenByte / 255.0;
        var b = blueByte / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta == 0.0)
        {
            return 0.0;
        }

        double hue;
        if (max == r)
        {
            hue = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            hue = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((r - g) / delta) + 4.0);
        }

        return hue < 0.0 ? hue + 360.0 : hue;
    }

    private static (byte Red, byte Green, byte Blue) Rgb(string colorHex)
    {
        var digits = colorHex.TrimStart('#');
        var offset = digits.Length == 8 ? 2 : 0;

        var red = byte.Parse(digits.AsSpan(offset, 2), NumberStyles.HexNumber);
        var green = byte.Parse(digits.AsSpan(offset + 2, 2), NumberStyles.HexNumber);
        var blue = byte.Parse(digits.AsSpan(offset + 4, 2), NumberStyles.HexNumber);

        return (red, green, blue);
    }
}
