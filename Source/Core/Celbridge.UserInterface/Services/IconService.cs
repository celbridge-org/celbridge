namespace Celbridge.UserInterface.Services;

public class IconService : IIconService
{
    private const string DefaultFileIconName = "nf-seti-default";
    private const string DefaultFolderIconName = "bs-folder-fill";
    private const string DefaultColor = "#9dc0ce";
    public const string DefaultFolderColor = "#FFCC40";

    private const string DefaultFontSize = "100%";
    private const string FallbackIconName = "bs-question-circle";

    private IReadOnlyDictionary<string, IconFontData> _fontsByPrefix = new Dictionary<string, IconFontData>();
    private IReadOnlyList<IconCatalogEntry> _supportedIcons = Array.Empty<IconCatalogEntry>();
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
        var loadResult = IconFontLoader.Load();
        if (loadResult.IsFailure)
        {
            return Result.Fail("Failed to load the bundled icon fonts")
                .WithErrors(loadResult);
        }

        _fontsByPrefix = loadResult.Value;
        _supportedIcons = BuildSupportedIcons();

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
        if (string.IsNullOrEmpty(fileName))
        {
            return Result<IconDefinition>.Ok(DefaultFileIcon);
        }

        if (_fileNameIconOverrides.TryGetValue(fileName, out var fileNameOverride))
        {
            return Result<IconDefinition>.Ok(fileNameOverride);
        }

        foreach (var suffix in GetExtensionSuffixes(fileName))
        {
            if (_fileIconOverrides.TryGetValue(suffix, out var suffixOverride))
            {
                return Result<IconDefinition>.Ok(suffixOverride);
            }
        }

        // A name matching no suffix falls through to the default file icon.
        return Result<IconDefinition>.Ok(DefaultFileIcon);
    }

    // The dot-free extension suffixes of a name, longest first: "code.editor.toml" yields
    // "editor.toml" then "toml", so a manifest takes its own icon rather than the one every TOML file
    // shares. A caller holding only an extension (".md") resolves through the same walk, because an
    // extension is its own longest suffix.
    private static IEnumerable<string> GetExtensionSuffixes(string fileName)
    {
        // A leading dot is not skipped, unlike editor resolution: a dotfile does carry an icon, and
        // ".gitignore" has always resolved through its own key.
        var searchFrom = 0;

        while (searchFrom < fileName.Length)
        {
            var dotIndex = fileName.IndexOf('.', searchFrom);
            if (dotIndex < 0)
            {
                yield break;
            }

            yield return fileName.Substring(dotIndex + 1);
            searchFrom = dotIndex + 1;
        }
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
        if (IconSymbolNames.TryGetIconName(icon, out var iconName))
        {
            return GetGlyph(iconName);
        }

        return FallbackGlyph();
    }

    public string GetIconName(IconSymbol icon)
    {
        if (IconSymbolNames.TryGetIconName(icon, out var iconName))
        {
            return iconName;
        }

        return string.Empty;
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

        if (!_fontsByPrefix.TryGetValue(prefix, out IconFontData? fontData))
        {
            return false;
        }

        if (!fontData.GlyphsByName.TryGetValue(unprefixedName, out string? fontCharacter))
        {
            return false;
        }

        glyph = new IconGlyph(fontCharacter, fontData.FontFamilyKey);

        return true;
    }

    public IReadOnlyList<IconCatalogEntry> GetSupportedIcons()
    {
        return _supportedIcons;
    }

    // The offered set only changes when the glyph and keyword maps are loaded, so it is built there rather
    // than rebuilt and re-sorted for every caller.
    private IReadOnlyList<IconCatalogEntry> BuildSupportedIcons()
    {
        var supportedIcons = new List<IconCatalogEntry>();

        foreach (var (prefix, fontData) in _fontsByPrefix)
        {
            if (!fontData.IsUserFacing)
            {
                continue;
            }

            foreach (var glyphName in fontData.GlyphsByName.Keys)
            {
                IReadOnlyList<string> keywords = Array.Empty<string>();
                if (fontData.KeywordsByName.TryGetValue(glyphName, out var namedKeywords))
                {
                    keywords = namedKeywords;
                }

                var iconName = $"{prefix}-{glyphName}";

                supportedIcons.Add(new IconCatalogEntry(iconName, keywords));
            }
        }

        supportedIcons.Sort((first, second) =>
            string.Compare(first.IconName, second.IconName, StringComparison.OrdinalIgnoreCase));

        return supportedIcons;
    }

    public bool IsSupportedIcon(string iconName)
    {
        var separatorIndex = iconName.IndexOf('-');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var prefix = iconName.Substring(0, separatorIndex);

        if (!_fontsByPrefix.TryGetValue(prefix, out IconFontData? fontData)
            || !fontData.IsUserFacing)
        {
            return false;
        }

        return TryGetGlyph(iconName, out _);
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
}
