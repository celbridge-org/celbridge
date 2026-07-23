using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// An icon font addressable by prefixed name, and the bundled asset holding its name to codepoint map.
/// </summary>
internal sealed record IconFontSet(string Prefix, string FontFamilyKey, string GlyphMapResource);

/// <summary>
/// The glyphs loaded from one icon font, keyed by their unprefixed name.
/// </summary>
internal sealed record IconFontGlyphs(string FontFamilyKey, IReadOnlyDictionary<string, string> GlyphsByName);

public class IconService : IIconService
{
    private const string DefaultFileIconName = "nf-seti-default";
    private const string DefaultFolderIconName = "bs-folder-fill";
    private const string DefaultColor = "#9dc0ce";
    public const string DefaultFolderColor = "#FFCC40";

    private const string DefaultFontSize = "100%";
    private const string FallbackIconName = "bs-question-circle";

    // The icon fonts addressable by a prefixed name. The prefix selects the font; the rest of the name
    // is looked up in that font's glyph map.
    private static readonly IReadOnlyList<IconFontSet> _iconFontSets = new List<IconFontSet>
    {
        new IconFontSet("bs", "BootstrapIconsFontFamily", "Assets.Fonts.BootstrapIcons.icon-glyphs.json"),
        new IconFontSet("nf", "NerdFontsFontFamily", "Assets.Fonts.NerdFonts.glyphnames.json")
    };

    // Maps each IconSymbol to a prefixed icon name. Add new common icons here. Anything not listed is
    // still resolvable by name.
    private static readonly Dictionary<IconSymbol, string> _symbolToIconName = new()
    {
        { IconSymbol.Close, "bs-x-lg" },
        { IconSymbol.Search, "bs-search" },
        { IconSymbol.Folder, "bs-folder" },
        { IconSymbol.FolderOpen, "bs-folder2-open" },
        { IconSymbol.FolderFilled, "bs-folder-fill" },
        { IconSymbol.FolderAdd, "bs-folder-plus" },
        { IconSymbol.FileAdd, "bs-file-earmark-plus" },
        { IconSymbol.File, "bs-file-earmark" },
        { IconSymbol.Bug, "bs-bug" },
        { IconSymbol.Back, "bs-arrow-left" },
        { IconSymbol.Forward, "bs-arrow-right" },
        { IconSymbol.Home, "bs-house" },
        { IconSymbol.Refresh, "bs-arrow-clockwise" },
        { IconSymbol.Reveal, "bs-box-arrow-up-right" },
        { IconSymbol.Delete, "bs-trash" },
        { IconSymbol.Error, "bs-exclamation-circle-fill" },
        { IconSymbol.Warning, "bs-exclamation-triangle-fill" },
        { IconSymbol.More, "bs-three-dots" },
        { IconSymbol.Collapse, "bs-arrows-collapse" },
        { IconSymbol.Settings, "bs-gear" },
        { IconSymbol.Sliders, "bs-sliders" },
        { IconSymbol.Windowed, "bs-window" },
        { IconSymbol.FullScreen, "bs-arrows-fullscreen" },
        { IconSymbol.FocusMode, "bs-fullscreen" },
        { IconSymbol.Presentation, "bs-easel" },
        { IconSymbol.Save, "bs-floppy" },
        { IconSymbol.ExitFullScreen, "bs-fullscreen-exit" },
        { IconSymbol.People, "bs-people" },
        { IconSymbol.Upload, "bs-upload" },
        { IconSymbol.ChevronDown, "bs-chevron-down" },
        { IconSymbol.ChevronRight, "bs-chevron-right" },
        { IconSymbol.ChevronUp, "bs-chevron-up" },
        { IconSymbol.MatchCase, "bs-type" },
        { IconSymbol.Replace, "bs-arrow-left-right" },
        { IconSymbol.Add, "bs-plus-lg" },
        { IconSymbol.Copy, "bs-copy" },
        { IconSymbol.Cut, "bs-scissors" },
        { IconSymbol.Paste, "bs-clipboard" },
        { IconSymbol.Rename, "bs-pencil" },
        { IconSymbol.Archive, "bs-archive" },
        { IconSymbol.Unarchive, "bs-box-arrow-up" },
        { IconSymbol.Recent, "bs-clock-history" },
        { IconSymbol.Menu, "bs-list" },
        { IconSymbol.Play, "bs-play-fill" },
        { IconSymbol.Examples, "bs-collection" },
        { IconSymbol.Exit, "bs-box-arrow-right" }
    };

    private Dictionary<string, IconFontGlyphs> _glyphsByPrefix = new();
    private IReadOnlyDictionary<string, IconDefinition> _fileIconOverrides =
        new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IconDefinition> _fileNameIconOverrides =
        new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);

    public IconDefinition DefaultFileIcon { get; private set; }
    public IconDefinition DefaultFolderIcon { get; private set; }

    public IconService()
    {
        var loadResult = LoadDefinitions();
        if (loadResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load icon definitions. {loadResult.DiagnosticReport}");
        }

        var getFileResult = CreateIcon(DefaultFileIconName, DefaultColor);
        if (getFileResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to build the default file icon. {getFileResult.DiagnosticReport}");
        }
        DefaultFileIcon = getFileResult.Value;

        var getFolderResult = CreateIcon(DefaultFolderIconName, DefaultFolderColor);
        if (getFolderResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to build the default folder icon. {getFolderResult.DiagnosticReport}");
        }
        DefaultFolderIcon = getFolderResult.Value;
    }

    public Result LoadDefinitions()
    {
        var loadGlyphsResult = LoadGlyphMaps();
        if (loadGlyphsResult.IsFailure)
        {
            return Result.Fail("Failed to load the icon font glyph maps")
                .WithErrors(loadGlyphsResult);
        }

        return Result.Ok();
    }

    public Result<IconDefinition> GetFileIconForExtension(string fileExtension)
    {
        if (fileExtension.StartsWith('.'))
        {
            // Remove leading dot before performing lookup
            fileExtension = fileExtension.Substring(1);
        }

        if (_fileIconOverrides.TryGetValue(fileExtension, out var overrideIcon))
        {
            return Result<IconDefinition>.Ok(overrideIcon);
        }

        // An extension with no override is drawn with the default file icon.
        return Result<IconDefinition>.Ok(DefaultFileIcon);
    }

    public Result<IconDefinition> GetFileIconForFileName(string fileName)
    {
        if (!string.IsNullOrEmpty(fileName) &&
            _fileNameIconOverrides.TryGetValue(fileName, out var fileNameOverride))
        {
            return Result<IconDefinition>.Ok(fileNameOverride);
        }

        // A name with no extension falls through to the default file icon.
        return GetFileIconForExtension(Path.GetExtension(fileName));
    }

    public Result<IconDefinition> CreateIcon(string iconName, string colorHex)
    {
        if (!TryGetGlyph(iconName, out var glyph))
        {
            return Result<IconDefinition>.Fail($"Unknown icon name: '{iconName}'.");
        }

        var color = DefaultColor;
        if (!string.IsNullOrEmpty(colorHex))
        {
            if (!IsHexColor(colorHex))
            {
                return Result<IconDefinition>.Fail(
                    $"Malformed icon colour: '{colorHex}'. Expected a hex colour such as \"#RRGGBB\" or \"#AARRGGBB\".");
            }
            color = colorHex;
        }

        var iconDefinition = new IconDefinition(glyph.FontCharacter, color, glyph.FontFamily, DefaultFontSize);

        return Result<IconDefinition>.Ok(iconDefinition);
    }

    public void SetFileIconOverrides(
        IReadOnlyDictionary<string, IconDefinition> extensionOverrides,
        IReadOnlyDictionary<string, IconDefinition> fileNameOverrides)
    {
        // Callers supply extensions in either form; the lookup keys on the dot-free form.
        var normalized = new Dictionary<string, IconDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var iconOverride in extensionOverrides)
        {
            var extension = iconOverride.Key.TrimStart('.');
            normalized[extension] = iconOverride.Value;
        }

        _fileIconOverrides = normalized;
        _fileNameIconOverrides = new Dictionary<string, IconDefinition>(fileNameOverrides, StringComparer.OrdinalIgnoreCase);
    }

    public IconGlyph GetGlyph(IconSymbol icon)
    {
        if (_symbolToIconName.TryGetValue(icon, out string? iconName))
        {
            return GetGlyph(iconName);
        }

        return FallbackGlyph();
    }

    public IconGlyph GetGlyph(string iconName)
    {
        if (TryGetGlyph(iconName, out IconGlyph glyph))
        {
            return glyph;
        }

        return FallbackGlyph();
    }

    public bool TryGetGlyph(string iconName, out IconGlyph glyph)
    {
        glyph = new IconGlyph(string.Empty, string.Empty);

        if (string.IsNullOrEmpty(iconName))
        {
            return false;
        }

        var separatorIndex = iconName.IndexOf('-');
        if (separatorIndex <= 0)
        {
            // Every icon name carries a font prefix, so an unprefixed name is not resolvable.
            return false;
        }

        var prefix = iconName.Substring(0, separatorIndex);
        var unprefixedName = iconName.Substring(separatorIndex + 1);

        if (!_glyphsByPrefix.TryGetValue(prefix, out IconFontGlyphs? fontGlyphs))
        {
            return false;
        }

        if (!fontGlyphs.GlyphsByName.TryGetValue(unprefixedName, out string? fontCharacter))
        {
            return false;
        }

        glyph = new IconGlyph(fontCharacter, fontGlyphs.FontFamilyKey);

        return true;
    }

    private IconGlyph FallbackGlyph()
    {
        if (TryGetGlyph(FallbackIconName, out IconGlyph fallback))
        {
            return fallback;
        }

        return new IconGlyph(string.Empty, string.Empty);
    }

    private static bool IsHexColor(string value)
    {
        if (!value.StartsWith('#'))
        {
            return false;
        }

        if (value.Length != 7 &&
            value.Length != 9)
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private Result LoadGlyphMaps()
    {
        var glyphsByPrefix = new Dictionary<string, IconFontGlyphs>();

        foreach (var iconFontSet in _iconFontSets)
        {
            var loadResult = LoadIconDataResource(iconFontSet.GlyphMapResource);
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to load the glyph map for icon font '{iconFontSet.Prefix}'.")
                    .WithErrors(loadResult);
            }
            var stream = loadResult.Value;

            var glyphsByName = new Dictionary<string, string>();

            try
            {
                using (var reader = new StreamReader(stream))
                {
                    var json = reader.ReadToEnd();
                    var glyphData = JsonNode.Parse(json) as JsonObject;
                    if (glyphData is null)
                    {
                        return Result.Fail($"Failed to parse the glyph map for icon font '{iconFontSet.Prefix}' as a JSON object.");
                    }

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
            }
            catch (Exception ex)
            {
                return Result.Fail($"An exception occurred when loading the glyph map for icon font '{iconFontSet.Prefix}'.")
                    .WithException(ex);
            }

            glyphsByPrefix[iconFontSet.Prefix] = new IconFontGlyphs(iconFontSet.FontFamilyKey, glyphsByName);
        }

        _glyphsByPrefix = glyphsByPrefix;

        return Result.Ok();
    }

    // Glyph maps are vendored byte-identical to their upstream projects, and the projects disagree on
    // shape: Bootstrap maps a name straight to a codepoint string, Nerd Fonts maps it to an object with a
    // "code" property. Returns empty for anything that is neither.
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

    private Result<Stream> LoadIconDataResource(string searchResourceName)
    {
        var entryAssembly = Assembly.GetAssembly(this.GetType());
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
            return Result<Stream>.Fail($"Resource '{resourceName}' not found.");
        }

        var resourceStream = entryAssembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            return Result<Stream>.Fail($"Failed to load resource '{resourceName}'.");
        }

        return Result<Stream>.Ok(resourceStream);
    }
}
