using System.Text.Json;
using Celbridge.Tests.Architecture;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Holds the bundled Nerd Fonts glyph set and the file type catalog together. The catalog names glyphs by
/// string, so a font upgrade that renames or drops one would otherwise show up as a wrong icon in the tree
/// rather than as a failure here.
/// </summary>
[TestFixture]
public class IconCatalogCoverageTests
{
    // The Nerd Fonts release the bundled font and glyph map both come from. Users pick icon names from the
    // cheat sheet at nerdfonts.com, which renders the current release, so this records which release the
    // application actually draws. Bump it with the font, never on its own.
    private const string BundledNerdFontsVersion = "3.5.0";

    [Test]
    public void EveryCatalogIconResolvesInTheBundledFont()
    {
        var iconService = new IconService();
        var unresolved = new List<string>();

        foreach (var (fileType, iconName) in ReadCatalogIcons())
        {
            if (!iconService.TryGetGlyph(iconName, out _))
            {
                unresolved.Add($"'{iconName}' named by '{fileType}'");
            }
        }

        unresolved.Should().BeEmpty(
            "a file type naming a glyph the bundled font does not carry draws the fallback icon instead");
    }

    [Test]
    public void TheGlyphMapReportsTheBundledRelease()
    {
        var glyphNames = ReadGlyphNamesDocument();
        var version = glyphNames.RootElement
            .GetProperty("METADATA")
            .GetProperty("version")
            .GetString();

        version.Should().Be(
            BundledNerdFontsVersion,
            "the glyph map and the font ship as a pair from one release, and the release decides what each "
            + "glyph looks like");
    }

    private static IReadOnlyList<(string FileType, string IconName)> ReadCatalogIcons()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var catalogPath = Path.Combine(
            sourceFolder, "Core", "Celbridge.WebHost", "Web", "celbridge-client", "file-types.json");
        File.Exists(catalogPath).Should().BeTrue($"the file type catalog should be at {catalogPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));

        var icons = new List<(string, string)>();
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object
                && entry.Value.TryGetProperty("icon", out var icon)
                && icon.GetString() is string iconName
                && iconName.Length > 0)
            {
                icons.Add((entry.Name, iconName));
            }
        }

        icons.Should().NotBeEmpty("the catalog is the source of every file icon");

        return icons;
    }

    private static JsonDocument ReadGlyphNamesDocument()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var glyphNamesPath = Path.Combine(
            sourceFolder, "Core", "Celbridge.UserInterface", "Assets", "Fonts", "NerdFonts", "glyphnames.json");
        File.Exists(glyphNamesPath).Should().BeTrue($"the bundled glyph map should be at {glyphNamesPath}");

        return JsonDocument.Parse(File.ReadAllText(glyphNamesPath));
    }
}
