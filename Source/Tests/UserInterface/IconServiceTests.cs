using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class IconServiceTests
{
    /// <summary>
    /// Guards against drift between the IconSymbol enum and the glyph map: every IconSymbol must resolve to
    /// a real glyph rather than the fallback. This fails if a symbol is missing from the IconService map,
    /// or if its icon name is not present in the font its prefix selects.
    /// </summary>
    [Test]
    public void EveryIconSymbolResolvesToARealGlyph()
    {
        var iconService = new IconService();
        var fallbackGlyph = iconService.GetGlyph("bs-this-icon-name-does-not-exist");

        foreach (IconSymbol iconSymbol in Enum.GetValues<IconSymbol>())
        {
            var glyph = iconService.GetGlyph(iconSymbol);

            Assert.That(glyph.FontCharacter, Is.Not.Empty,
                $"IconSymbol.{iconSymbol} did not resolve to a glyph.");
            Assert.That(glyph, Is.Not.EqualTo(fallbackGlyph),
                $"IconSymbol.{iconSymbol} resolved to the fallback glyph; it is missing from the IconService map or its icon name is not in the icon font.");
        }
    }

    [Test]
    public void TryGetGlyph_KnownName_ResolvesToTheFontItsPrefixSelects()
    {
        var iconService = new IconService();

        var found = iconService.TryGetGlyph("bs-journal-text", out var glyph);

        found.Should().BeTrue();
        glyph.FontCharacter.Should().NotBeEmpty();
        glyph.FontFamily.Should().Be("BootstrapIconsFontFamily");
    }

    [TestCase("journal-text", Description = "unprefixed name rejected")]
    [TestCase("zz-journal-text", Description = "unknown font prefix rejected")]
    [TestCase("bs-no-such-icon", Description = "unknown name within a known font rejected")]
    [TestCase("", Description = "empty name rejected")]
    public void TryGetGlyph_UnresolvableName_ReturnsFalse(string iconName)
    {
        var iconService = new IconService();

        iconService.TryGetGlyph(iconName, out _).Should().BeFalse();
    }

    /// <summary>
    /// The Nerd Fonts map is vendored in its upstream shape, which nests the codepoint in an object and
    /// carries a metadata block that is not a glyph. This covers both against a regression in the loader.
    /// </summary>
    [TestCase("nf-oct-repo", Description = "Octicons")]
    [TestCase("nf-seti-json", Description = "Seti UI")]
    [TestCase("nf-dev-git", Description = "Devicons")]
    public void TryGetGlyph_NerdFontsName_ResolvesIntoTheNerdFont(string iconName)
    {
        var iconService = new IconService();

        var found = iconService.TryGetGlyph(iconName, out var glyph);

        found.Should().BeTrue();
        glyph.FontCharacter.Should().NotBeEmpty();
        glyph.FontFamily.Should().Be("NerdFontsFontFamily");
    }

    /// <summary>
    /// The Nerd Fonts Material Design block sits above the BMP, so its glyphs are surrogate pairs. A
    /// truncating codepoint conversion would silently yield the wrong character.
    /// </summary>
    [Test]
    public void TryGetGlyph_AboveBmpName_ResolvesToASurrogatePair()
    {
        var iconService = new IconService();

        var found = iconService.TryGetGlyph("nf-md-language_python", out var glyph);

        found.Should().BeTrue();
        glyph.FontCharacter.Length.Should().Be(2);
        char.IsSurrogatePair(glyph.FontCharacter[0], glyph.FontCharacter[1]).Should().BeTrue();
    }

    [Test]
    public void TryGetGlyph_NerdFontsMetadataBlock_IsNotAGlyph()
    {
        var iconService = new IconService();

        iconService.TryGetGlyph("nf-METADATA", out _).Should().BeFalse();
    }

    [Test]
    public void CreateIcon_KnownName_UsesThePrefixedFontAndSuppliedColour()
    {
        var iconService = new IconService();

        var result = iconService.CreateIcon("bs-journal-text", "#FF8800");

        result.IsSuccess.Should().BeTrue();
        var icon = result.Value;
        icon.FontFamily.Should().Be("BootstrapIconsFontFamily");
        icon.FontColor.Should().Be("#FF8800");
        icon.FontCharacter.Should().Be(iconService.GetGlyph("bs-journal-text").FontCharacter);
    }

    [Test]
    public void CreateIcon_NoColour_FallsBackToTheThemeColour()
    {
        var iconService = new IconService();

        var result = iconService.CreateIcon("bs-journal-text", string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.FontColor.Should().NotBeEmpty();
    }

    [TestCase("bs-no-such-icon", "#FF8800", Description = "unknown icon rejected")]
    [TestCase("journal-text", "#FF8800", Description = "unprefixed name rejected")]
    [TestCase("bs-journal-text", "FF8800", Description = "colour without a hash rejected")]
    [TestCase("bs-journal-text", "#FF88", Description = "wrong-length colour rejected")]
    [TestCase("bs-journal-text", "#GGGGGG", Description = "non-hex colour rejected")]
    public void CreateIcon_UnusableInput_ReturnsFailure(string iconName, string colorHex)
    {
        var iconService = new IconService();

        iconService.CreateIcon(iconName, colorHex).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// A file with no usable extension is recognised by its whole name, and a name override outranks the
    /// override registered for the same file's extension.
    /// </summary>
    [Test]
    public void GetFileIconForFileName_NameOverrideWins_OverExtensionAndTheme()
    {
        var iconService = new IconService();
        var nameIcon = iconService.CreateIcon("bs-journal-text", "#FF8800").Value;
        var extensionIcon = iconService.CreateIcon("bs-gear", "#00FF00").Value;

        iconService.SetFileIconOverrides(
            new Dictionary<string, IconDefinition> { [".json"] = extensionIcon },
            new Dictionary<string, IconDefinition> { ["Makefile"] = nameIcon, ["package.json"] = nameIcon });

        // No extension to fall back on, so only the name lookup can match.
        iconService.GetFileIconForFileName("Makefile").Value.Should().Be(nameIcon);
        iconService.GetFileIconForFileName("makefile").Value.Should().Be(nameIcon);

        // The name wins even though the extension also has an override.
        iconService.GetFileIconForFileName("package.json").Value.Should().Be(nameIcon);
        iconService.GetFileIconForFileName("other.json").Value.Should().Be(extensionIcon);

        // An unknown extensionless name falls through to the default file icon.
        iconService.GetFileIconForFileName("LICENSE").Value.Should().Be(iconService.DefaultFileIcon);
    }

    [Test]
    public void GetFileIconForExtension_OverrideWins_AndEachSetReplacesTheLast()
    {
        var iconService = new IconService();
        var themeIcon = iconService.GetFileIconForExtension(".cs").Value;
        var glyphIcon = iconService.CreateIcon("bs-journal-text", "#FF8800").Value;

        iconService.SetFileIconOverrides(new Dictionary<string, IconDefinition> { [".cs"] = glyphIcon }, new Dictionary<string, IconDefinition>());

        // The resource tree looks up the dot-free form; the resource picker uses the dotted form.
        iconService.GetFileIconForExtension(".cs").Value.Should().Be(glyphIcon);
        iconService.GetFileIconForExtension("cs").Value.Should().Be(glyphIcon);

        // A later discovery pass supplies the whole set, so an override it omits is gone.
        iconService.SetFileIconOverrides(new Dictionary<string, IconDefinition>(), new Dictionary<string, IconDefinition>());
        iconService.GetFileIconForExtension(".cs").Value.Should().Be(themeIcon);
    }
}
