using System.Text.Json;
using System.Text.RegularExpressions;
using Celbridge.Tests.Architecture;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Holds the bundled icon fonts together with the data written against them: the file type catalog over
/// the Nerd Fonts glyph set, and the search keywords over the Bootstrap glyph set. Both name glyphs by
/// string, so a font upgrade that renames or drops one would otherwise show up as a wrong icon in the tree
/// or a missing icon in the picker rather than as a failure here.
/// </summary>
[TestFixture]
public class IconCatalogCoverageTests
{
    // The Nerd Fonts release the bundled font and glyph map both come from. Users pick icon names from the
    // cheat sheet at nerdfonts.com, which renders the current release, so this records which release the
    // application actually draws. Bump it with the font, never on its own.
    private const string BundledNerdFontsVersion = "3.5.0";

    // The Bootstrap Icons release the bundled font, glyph map and keyword map all come from. The keywords
    // describe the icons that release carries, so they are refreshed with the font rather than separately.
    private const string BundledBootstrapIconsVersion = "1.12.1";

    // The keyword map nests its data under this property, alongside the release it was generated from.
    private const string KeywordsProperty = "keywords";

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

    /// <summary>
    /// The keyword map is generated against the bundled glyph map, so a keyword file left behind by an
    /// earlier font would offer the picker a name the bundled font cannot draw.
    /// </summary>
    [Test]
    public void EveryKeywordedIconIsInTheBundledFont()
    {
        using var glyphs = ReadBootstrapDocument("icon-glyphs.json");
        using var keywords = ReadBootstrapDocument("icon-keywords.json");

        var glyphNames = glyphs.RootElement
            .EnumerateObject()
            .Select(glyph => glyph.Name)
            .ToHashSet();

        var unknownNames = keywords.RootElement
            .GetProperty(KeywordsProperty)
            .EnumerateObject()
            .Select(keyword => keyword.Name)
            .Where(iconName => !glyphNames.Contains(iconName))
            .ToList();

        unknownNames.Should().BeEmpty(
            "the keyword map is generated from the bundled glyph map, so it cannot name an icon the font "
            + "does not carry");
    }

    /// <summary>
    /// The keyword the search matches has to be the lowercase form the generator writes, because the
    /// search term is lowercased before the comparison rather than compared case-insensitively.
    /// </summary>
    [Test]
    public void EveryKeywordIsLowercaseAndNonEmpty()
    {
        using var keywords = ReadBootstrapDocument("icon-keywords.json");

        foreach (var icon in keywords.RootElement.GetProperty(KeywordsProperty).EnumerateObject())
        {
            icon.Value.ValueKind.Should().Be(JsonValueKind.Array, $"'{icon.Name}' should hold a list of keywords");
            icon.Value.GetArrayLength().Should().BeGreaterThan(0, $"'{icon.Name}' should be absent rather than empty");

            foreach (var keyword in icon.Value.EnumerateArray())
            {
                var keywordText = keyword.GetString();
                keywordText.Should().NotBeNullOrWhiteSpace();
                keywordText.Should().Be(keywordText!.ToLowerInvariant(), $"'{icon.Name}' carries a keyword that is not lowercase");
            }
        }
    }

    /// <summary>
    /// The font, the glyph map and the keyword map ship as one release. The vendored stylesheet is where
    /// that release records itself, so it is what the pinned version is checked against.
    /// </summary>
    [Test]
    public void TheVendoredStylesheetReportsTheBundledRelease()
    {
        var header = File.ReadAllText(BootstrapStylesheetPath());

        header.Should().Contain(
            $"Bootstrap Icons v{BundledBootstrapIconsVersion}",
            "the keyword map is generated for the bundled release, so the two are upgraded together");
    }

    /// <summary>
    /// The keyword map records the release it was generated from, so a map left behind by a font upgrade
    /// fails here rather than shipping keywords that describe a different set of icons.
    /// </summary>
    [Test]
    public void TheKeywordMapRecordsTheBundledRelease()
    {
        using var keywords = ReadBootstrapDocument("icon-keywords.json");

        var release = keywords.RootElement.GetProperty("release").GetString();

        release.Should().Be(
            $"v{BundledBootstrapIconsVersion}",
            "regenerating the keyword map is part of a font upgrade, not a separate decision");
    }

    /// <summary>
    /// The picker offers the icons the glyph map carries, and a WebView draws them from the vendored
    /// stylesheet rather than from that map. An icon the stylesheet has no rule for would be offered on
    /// both surfaces and draw on only one.
    /// </summary>
    [Test]
    public void TheVendoredStylesheetDrawsEveryBundledIcon()
    {
        using var glyphs = ReadBootstrapDocument("icon-glyphs.json");

        var stylesheet = File.ReadAllText(BootstrapStylesheetPath());
        var styledNames = Regex.Matches(stylesheet, @"\.bi-([a-z0-9-]+)::before")
            .Select(match => match.Groups[1].Value)
            .ToHashSet();

        var unstyledNames = glyphs.RootElement
            .EnumerateObject()
            .Select(glyph => glyph.Name)
            .Where(iconName => !styledNames.Contains(iconName))
            .ToList();

        unstyledNames.Should().BeEmpty(
            "every icon the picker offers has to draw in a WebView as well as natively");
    }

    private static string BootstrapStylesheetPath()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var stylesheetPath = Path.Combine(
            sourceFolder, "Core", "Celbridge.WebHost", "Web", "bootstrap-icons", "bootstrap-icons.css");
        File.Exists(stylesheetPath).Should().BeTrue($"the vendored stylesheet should be at {stylesheetPath}");

        return stylesheetPath;
    }

    private static JsonDocument ReadBootstrapDocument(string fileName)
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var documentPath = Path.Combine(
            sourceFolder, "Core", "Celbridge.UserInterface", "Assets", "Fonts", "BootstrapIcons", fileName);
        File.Exists(documentPath).Should().BeTrue($"the bundled icon data should be at {documentPath}");

        return JsonDocument.Parse(File.ReadAllText(documentPath));
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
