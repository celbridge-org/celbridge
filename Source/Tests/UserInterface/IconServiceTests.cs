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
}
