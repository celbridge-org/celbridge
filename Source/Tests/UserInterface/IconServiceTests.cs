using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class IconServiceTests
{
    /// <summary>
    /// Guards against drift between the IconSymbol enum and the glyph map: every IconSymbol must resolve to
    /// a real glyph rather than the fallback. This fails if a kind is missing from the IconService map,
    /// or if its glyph name is not present in the bundled icon font.
    /// </summary>
    [Test]
    public void EveryIconSymbolResolvesToARealGlyph()
    {
        var iconService = new IconService();
        var fallbackGlyph = iconService.GetGlyph("this-glyph-name-does-not-exist");

        foreach (IconSymbol iconSymbol in Enum.GetValues<IconSymbol>())
        {
            var glyph = iconService.GetGlyph(iconSymbol);

            Assert.That(glyph, Is.Not.Empty,
                $"IconSymbol.{iconSymbol} did not resolve to a glyph.");
            Assert.That(glyph, Is.Not.EqualTo(fallbackGlyph),
                $"IconSymbol.{iconSymbol} resolved to the fallback glyph; it is missing from the IconService map or its glyph name is not in the icon font.");
        }
    }

    [Test]
    public void CreateGlyphFileIcon_KnownGlyph_UsesTheSharedIconFontAndSuppliedColour()
    {
        var iconService = new IconService();

        var result = iconService.CreateGlyphFileIcon("journal-text", "#FF8800");

        result.IsSuccess.Should().BeTrue();
        var icon = result.Value;
        icon.FontFamily.Should().Be("BootstrapIconsFontFamily");
        icon.FontColor.Should().Be("#FF8800");
        icon.FontCharacter.Should().Be(iconService.GetGlyph("journal-text"));
    }

    [Test]
    public void CreateGlyphFileIcon_NoColour_FallsBackToTheThemeColour()
    {
        var iconService = new IconService();

        var result = iconService.CreateGlyphFileIcon("journal-text", string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.FontColor.Should().NotBeEmpty();
    }

    [TestCase("no-such-glyph-name", "#FF8800", Description = "unknown glyph rejected")]
    [TestCase("journal-text", "FF8800", Description = "colour without a hash rejected")]
    [TestCase("journal-text", "#FF88", Description = "wrong-length colour rejected")]
    [TestCase("journal-text", "#GGGGGG", Description = "non-hex colour rejected")]
    public void CreateGlyphFileIcon_UnusableInput_ReturnsFailure(string glyphName, string colorHex)
    {
        var iconService = new IconService();

        iconService.CreateGlyphFileIcon(glyphName, colorHex).IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetFileIconForExtension_OverrideWins_AndEachSetReplacesTheLast()
    {
        var iconService = new IconService();
        var themeIcon = iconService.GetFileIconForExtension(".cs").Value;
        var glyphIcon = iconService.CreateGlyphFileIcon("journal-text", "#FF8800").Value;

        iconService.SetFileIconOverrides(new Dictionary<string, FileIconDefinition> { [".cs"] = glyphIcon });

        // The resource tree looks up the dot-free form; the resource picker uses the dotted form.
        iconService.GetFileIconForExtension(".cs").Value.Should().Be(glyphIcon);
        iconService.GetFileIconForExtension("cs").Value.Should().Be(glyphIcon);

        // A later discovery pass supplies the whole set, so an override it omits is gone.
        iconService.SetFileIconOverrides(new Dictionary<string, FileIconDefinition>());
        iconService.GetFileIconForExtension(".cs").Value.Should().Be(themeIcon);
    }
}
