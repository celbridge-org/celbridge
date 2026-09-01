using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// An icon font addressable by prefixed name, and the bundled assets holding its name to codepoint map and
/// the search keywords for its icons. A font the user is not offered as a choice carries no keywords.
/// </summary>
internal sealed record IconFontSet(
    string Prefix,
    string FontFamilyKey,
    string GlyphMapResource,
    string KeywordMapResource,
    bool IsUserFacing);

/// <summary>
/// One icon font as loaded from its bundled assets: the font family its glyphs are drawn with, whether the
/// user is offered its icons as a choice, and its glyphs and search keywords keyed by unprefixed name.
/// </summary>
internal sealed record IconFontData(
    string FontFamilyKey,
    bool IsUserFacing,
    IReadOnlyDictionary<string, string> GlyphsByName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> KeywordsByName);

/// <summary>
/// Reads the bundled icon font assets into the data the icon service resolves names through. What the
/// vendored file shapes imply is held here, so the service itself deals only in loaded icons.
/// </summary>
internal static class IconFontLoader
{
    // The keyword map nests its data under this property, alongside the icon font release it was generated
    // from.
    private const string KeywordMapKeywordsProperty = "keywords";

    // The icon fonts addressable by a prefixed name. The prefix selects the font; the rest of the name is
    // looked up in that font's glyph map.
    private static readonly IReadOnlyList<IconFontSet> _iconFontSets = new List<IconFontSet>
    {
        new IconFontSet(
            "bs",
            "BootstrapIconsFontFamily",
            "Assets.Fonts.BootstrapIcons.icon-glyphs.json",
            "Assets.Fonts.BootstrapIcons.icon-keywords.json",
            IsUserFacing: true),

        // Bundled as the host's own file type icon theme rather than as a vocabulary users choose from:
        // web content is not served this font, so a name picked from it would draw natively and fail in
        // an HTML editor.
        new IconFontSet(
            "nf",
            "NerdFontsFontFamily",
            "Assets.Fonts.NerdFonts.glyphnames.json",
            string.Empty,
            IsUserFacing: false)
    };

    /// <summary>
    /// Loads every bundled icon font, keyed by the prefix that font's icon names carry.
    /// </summary>
    public static Result<IReadOnlyDictionary<string, IconFontData>> Load()
    {
        var fontsByPrefix = new Dictionary<string, IconFontData>();

        foreach (var iconFontSet in _iconFontSets)
        {
            var loadResult = LoadFont(iconFontSet);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load the icon font '{iconFontSet.Prefix}'.")
                    .WithErrors(loadResult);
            }
            var fontData = loadResult.Value;

            fontsByPrefix[iconFontSet.Prefix] = fontData;
        }

        return fontsByPrefix.OkResult<IReadOnlyDictionary<string, IconFontData>>();
    }

    private static Result<IconFontData> LoadFont(IconFontSet iconFontSet)
    {
        var readGlyphsResult = ReadGlyphMap(iconFontSet);
        if (readGlyphsResult.IsFailure)
        {
            return Result.Fail("Failed to read the glyph map.")
                .WithErrors(readGlyphsResult);
        }
        var glyphsByName = readGlyphsResult.Value;

        var readKeywordsResult = ReadKeywordMap(iconFontSet);
        if (readKeywordsResult.IsFailure)
        {
            return Result.Fail("Failed to read the keyword map.")
                .WithErrors(readKeywordsResult);
        }
        var keywordsByName = readKeywordsResult.Value;

        return new IconFontData(
            iconFontSet.FontFamilyKey,
            iconFontSet.IsUserFacing,
            glyphsByName,
            keywordsByName);
    }

    private static Result<Dictionary<string, string>> ReadGlyphMap(IconFontSet iconFontSet)
    {
        var mapDescription = $"glyph map for icon font '{iconFontSet.Prefix}'";

        var readResult = ReadIconDataObject(iconFontSet.GlyphMapResource, mapDescription);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read the {mapDescription}.")
                .WithErrors(readResult);
        }
        var glyphData = readResult.Value;

        var glyphsByName = new Dictionary<string, string>();

        try
        {
            foreach (var kv in glyphData)
            {
                var code = ReadCodePoint(kv.Value);
                if (string.IsNullOrEmpty(code))
                {
                    // A non-glyph entry, such as the metadata block the Nerd Fonts map carries.
                    continue;
                }

                int codePoint = int.Parse(code, NumberStyles.HexNumber);

                glyphsByName[kv.Key] = char.ConvertFromUtf32(codePoint);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when reading the codepoints in the {mapDescription}.")
                .WithException(ex);
        }

        return glyphsByName;
    }

    private static Result<Dictionary<string, IReadOnlyList<string>>> ReadKeywordMap(IconFontSet iconFontSet)
    {
        var keywordsByName = new Dictionary<string, IReadOnlyList<string>>();

        // A font whose icons are never offered as a choice is never searched, so it ships no keywords.
        if (string.IsNullOrEmpty(iconFontSet.KeywordMapResource))
        {
            return keywordsByName;
        }

        var mapDescription = $"keyword map for icon font '{iconFontSet.Prefix}'";

        var readResult = ReadIconDataObject(iconFontSet.KeywordMapResource, mapDescription);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read the {mapDescription}.")
                .WithErrors(readResult);
        }
        var keywordDocument = readResult.Value;

        // The keywords sit under their own property so the file can also record the icon font release they
        // were generated from.
        if (keywordDocument[KeywordMapKeywordsProperty] is not JsonObject keywordData)
        {
            return Result.Fail($"The {mapDescription} does not hold a '{KeywordMapKeywordsProperty}' object.");
        }

        foreach (var kv in keywordData)
        {
            if (kv.Value is not JsonArray keywordArray)
            {
                return Result.Fail($"Icon '{kv.Key}' in the {mapDescription} is not a list of keywords.");
            }

            var keywords = new List<string>();
            foreach (var keyword in keywordArray)
            {
                if (keyword is null)
                {
                    continue;
                }

                keywords.Add(keyword.ToString());
            }

            keywordsByName[kv.Key] = keywords;
        }

        return keywordsByName;
    }

    // The icon data files are read the same way and differ only in how an entry is converted, so the load,
    // the parse and the failure reporting are shared. The description names the file in any error.
    private static Result<JsonObject> ReadIconDataObject(string resourceName, string mapDescription)
    {
        var loadResult = LoadIconDataResource(resourceName);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load the {mapDescription}.")
                .WithErrors(loadResult);
        }
        var stream = loadResult.Value;

        try
        {
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var dataObject = JsonNode.Parse(json) as JsonObject;
            if (dataObject is null)
            {
                return Result.Fail($"Failed to parse the {mapDescription} as a JSON object.");
            }

            return dataObject;
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when reading the {mapDescription}.")
                .WithException(ex);
        }
    }

    // The vendored glyph maps disagree on shape: Bootstrap maps a name straight to a hex codepoint string,
    // Nerd Fonts maps it to an object with a "code" property. Returns empty for anything that is neither.
    private static string ReadCodePoint(JsonNode? value)
    {
        if (value is JsonValue codeValue)
        {
            return codeValue.ToString();
        }

        if (value is JsonObject glyphObject &&
            glyphObject.TryGetPropertyValue("code", out var codeProperty) &&
            codeProperty is not null)
        {
            return codeProperty.ToString();
        }

        return string.Empty;
    }

    private static Result<Stream> LoadIconDataResource(string searchResourceName)
    {
        var entryAssembly = Assembly.GetAssembly(typeof(IconFontLoader));
        Guard.IsNotNull(entryAssembly);

        // The name is prepended with the assembly name so look for a resource that
        // ends with the requested resource name.

        string resourceName = string.Empty;
        string[] names = entryAssembly.GetManifestResourceNames();
        foreach (var name in names)
        {
            if (name.EndsWith(searchResourceName))
            {
                resourceName = name;
                break;
            }
        }

        if (string.IsNullOrEmpty(resourceName))
        {
            return Result<Stream>.Fail($"Resource '{searchResourceName}' not found.");
        }

        var resourceStream = entryAssembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            return Result<Stream>.Fail($"Failed to load resource '{resourceName}'.");
        }

        return Result<Stream>.Ok(resourceStream);
    }
}
