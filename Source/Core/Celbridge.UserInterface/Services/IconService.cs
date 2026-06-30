using System.Reflection;
using System.Text.Json.Nodes;

namespace Celbridge.UserInterface.Services;

public class IconService : IIconService
{
    private const string DefaultFileIconName = "_file";
    private const string DefaultFolderIconName = "_folder";
    private const string DefaultColor = "#9dc0ce";
    public const string DefaultFolderColor = "#FFCC40";

    private const string FileIconsThemeResource = "Assets.Fonts.FileIcons.file-icons-icon-theme.json";
    private const string IconGlyphsResource = "Assets.Fonts.BootstrapIcons.icon-glyphs.json";
    private const string FallbackGlyphName = "question-circle";

    private Dictionary<string, string> _fileExtensionDefinitions = new();
    private Dictionary<string, FileIconDefinition> _iconDefinitions = new();
    private Dictionary<string, string> _glyphsByName = new();

    // Maps each IconSymbol to its glyph name in the bundled icon font (Bootstrap Icons). The name is
    // resolved to a glyph code via the bundled icon-glyphs.json map. Add new common icons here.
    // Anything not listed is still resolvable by glyph name.
    private static readonly Dictionary<IconSymbol, string> _kindToGlyphName = new()
    {
        { IconSymbol.Close, "x-lg" },
        { IconSymbol.Search, "search" },
        { IconSymbol.Folder, "folder" },
        { IconSymbol.FolderOpen, "folder2-open" },
        { IconSymbol.FolderFilled, "folder-fill" },
        { IconSymbol.FolderAdd, "folder-plus" },
        { IconSymbol.FileAdd, "file-earmark-plus" },
        { IconSymbol.File, "file-earmark" },
        { IconSymbol.Bug, "bug" },
        { IconSymbol.Back, "arrow-left" },
        { IconSymbol.Forward, "arrow-right" },
        { IconSymbol.Home, "house" },
        { IconSymbol.Refresh, "arrow-clockwise" },
        { IconSymbol.Reveal, "box-arrow-up-right" },
        { IconSymbol.Delete, "trash" },
        { IconSymbol.Error, "exclamation-circle-fill" },
        { IconSymbol.Warning, "exclamation-triangle-fill" },
        { IconSymbol.More, "three-dots" },
        { IconSymbol.Collapse, "arrows-collapse" },
        { IconSymbol.Settings, "gear" },
        { IconSymbol.Windowed, "window" },
        { IconSymbol.FullScreen, "arrows-fullscreen" },
        { IconSymbol.ZenMode, "fullscreen" },
        { IconSymbol.Presenter, "easel" },
        { IconSymbol.Save, "floppy" },
        { IconSymbol.ExitFullScreen, "fullscreen-exit" },
        { IconSymbol.People, "people" },
        { IconSymbol.Upload, "upload" },
        { IconSymbol.ChevronDown, "chevron-down" },
        { IconSymbol.ChevronRight, "chevron-right" },
        { IconSymbol.ChevronUp, "chevron-up" },
        { IconSymbol.MatchCase, "type" },
        { IconSymbol.Replace, "arrow-left-right" },
        { IconSymbol.Add, "plus-lg" },
        { IconSymbol.Copy, "copy" },
        { IconSymbol.Cut, "scissors" },
        { IconSymbol.Paste, "clipboard" },
        { IconSymbol.Rename, "pencil" },
        { IconSymbol.Archive, "archive" },
        { IconSymbol.Unarchive, "box-arrow-up" },
        { IconSymbol.Recent, "clock-history" },
        { IconSymbol.Menu, "list" },
        { IconSymbol.Play, "play-fill" },
        { IconSymbol.Examples, "collection" },
        { IconSymbol.Exit, "box-arrow-right" }
    };

    public FileIconDefinition DefaultFileIcon { get; private set; }
    public FileIconDefinition DefaultFolderIcon { get; private set; }

    public IconService()
    {
        var loadResult = LoadDefinitions();
        if (loadResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load file icon definitions. {loadResult.DiagnosticReport}");
        }

        var getFileResult = GetFileIcon(DefaultFileIconName);
        if (getFileResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to get default file icon definitions. {getFileResult.DiagnosticReport}");
        }
        DefaultFileIcon = getFileResult.Value;

        var getFolderResult = GetFileIcon(DefaultFolderIconName);
        if (getFolderResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to get default folder icon definitions. {getFolderResult.DiagnosticReport}");
        }
        DefaultFolderIcon = getFolderResult.Value;

        var loadGlyphsResult = LoadGlyphMap();
        if (loadGlyphsResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load icon glyph map. {loadGlyphsResult.DiagnosticReport}");
        }
    }

    public Result LoadDefinitions()
    {
        var loadResult = LoadIconData();
        if (loadResult.IsFailure)
        {
            return Result.Fail("Failed to load file icon definition")
                .WithErrors(loadResult);
        }
        var iconData = loadResult.Value;

        try
        {
            PopulateIconDefinitions(iconData);
            PopulateFileExtensionDefinitions(iconData);
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading the file icon definitions.")
                .WithException(ex);
        }

        return Result.Ok();
    }

    public Result<FileIconDefinition> GetFileIcon(string iconName)
    {
        if (!_iconDefinitions.TryGetValue(iconName, out FileIconDefinition? iconDefinition))
        {
            if (_iconDefinitions.TryGetValue(DefaultFileIconName, out FileIconDefinition? defaultIcon))
            {
                // Icon definition not found, return default icon
                return Result<FileIconDefinition>.Ok(defaultIcon);
            }

            return Result<FileIconDefinition>.Fail($"No default icon found.");
        }

        return Result<FileIconDefinition>.Ok(iconDefinition);
    }

    public Result<FileIconDefinition> GetFileIconForExtension(string fileExtension)
    {
        if (fileExtension.StartsWith('.'))
        {
            // Remove leading dot before performing lookup
            fileExtension = fileExtension.Substring(1);
        }

        if (!_fileExtensionDefinitions.TryGetValue(fileExtension, out string? iconName))
        {
            if (_iconDefinitions.TryGetValue(DefaultFileIconName, out FileIconDefinition? defaultIcon))
            {
                // File extension not recognized, return default icon
                return Result<FileIconDefinition>.Ok(defaultIcon);
            }

            return Result<FileIconDefinition>.Fail($"No default icon found.");
        }

        return GetFileIcon(iconName);
    }

    public FileIconDefinition GetDefaultFileIcon()
    {
        if (_iconDefinitions.TryGetValue(DefaultFileIconName, out FileIconDefinition? defaultIcon))
        {
            return defaultIcon;
        }

        throw new InvalidOperationException();
    }

    public string IconFontFamilyUri => "ms-appx:///Celbridge.UserInterface/Assets/Fonts/BootstrapIcons/bootstrap-icons.ttf#bootstrap-icons";

    public string GetGlyph(IconSymbol icon)
    {
        if (_kindToGlyphName.TryGetValue(icon, out string? glyphName))
        {
            return GetGlyph(glyphName);
        }

        return FallbackGlyph();
    }

    public string GetGlyph(string glyphName)
    {
        if (TryGetGlyph(glyphName, out string glyph))
        {
            return glyph;
        }

        return FallbackGlyph();
    }

    public bool TryGetGlyph(string glyphName, out string glyph)
    {
        if (!string.IsNullOrEmpty(glyphName) &&
            _glyphsByName.TryGetValue(glyphName, out string? found))
        {
            glyph = found;
            return true;
        }

        glyph = string.Empty;
        return false;
    }

    private string FallbackGlyph()
    {
        if (_glyphsByName.TryGetValue(FallbackGlyphName, out string? fallback))
        {
            return fallback;
        }

        return string.Empty;
    }

    private Result LoadGlyphMap()
    {
        var loadResult = LoadIconDataResource(IconGlyphsResource);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load icon glyph map from resource '{IconGlyphsResource}'.")
                .WithErrors(loadResult);
        }
        var stream = loadResult.Value;

        try
        {
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var glyphData = JsonNode.Parse(json) as JsonObject;
                if (glyphData is null)
                {
                    return Result.Fail("Failed to parse the icon glyph map as a JSON object.");
                }

                foreach (var kv in glyphData)
                {
                    Guard.IsNotNull(kv.Value);

                    string code = kv.Value.ToString();
                    int codePoint = int.Parse(code, System.Globalization.NumberStyles.HexNumber);
                    string glyph = ((char)codePoint).ToString();

                    _glyphsByName[kv.Key] = glyph;
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail("An exception occurred when loading the icon glyph map.")
                .WithException(ex);
        }

        return Result.Ok();
    }

    private void PopulateIconDefinitions(JsonObject iconData)
    {
        var iconDefinitions = iconData["iconDefinitions"] as JsonObject;
        Guard.IsNotNull(iconDefinitions);

        foreach (var kv in iconDefinitions)
        {
            Guard.IsNotNull(kv.Value);

            string iconName = kv.Key;
            var iconProperties = kv.Value as JsonObject;
            Guard.IsNotNull(iconProperties);

            string fontId;
            if (iconProperties.ContainsKey("fontId"))
            {
                fontId = iconProperties["fontId"]!.ToString();
            }
            else
            {
                // Not a valid icon definition
                continue;
            }

            // Map fontId to a FontFamily key
            string fontFamily;
            switch (fontId)
            {
                case "fi":
                    fontFamily = "FileIconsFontFamily";
                    break;
                case "fa":
                    fontFamily = "FontAwesomeFontFamily";
                    break;
                case "mf":
                    fontFamily = "MFixxFontFamily";
                    break;
                case "devicons":
                    fontFamily = "DevOpIconsFontFamily";
                    break;
                case "octicons":
                    fontFamily = "OctIconsFontFamily";
                    break;
                default:
                    // Not a valid icon definition
                    continue;
            }

            string character = iconProperties["fontCharacter"]!.ToString();
            if (string.IsNullOrEmpty(character))
            {
                continue;
            }

            string fontCharacter;
            if (character.Length == 1)
            {
                fontCharacter = character;
            }
            else
            {
                fontCharacter = ConvertUnicodeString(character);
            }

            string color;
            if (iconProperties.ContainsKey("fontColor"))
            {
                color = iconProperties["fontColor"]!.ToString();
            }
            else
            {
                color = DefaultColor;
            }

            string fontSize;
            if (iconProperties.ContainsKey("fontSize"))
            {
                fontSize = iconProperties["fontSize"]!.ToString();
            }
            else
            {
                fontSize = "100%";
            }

            var iconDefinition = new FileIconDefinition(fontCharacter, color, fontFamily, fontSize);

            _iconDefinitions.Add(iconName, iconDefinition);
        }
    }

    private static string ConvertUnicodeString(string unicodeInput)
    {
        if (unicodeInput.StartsWith("\\"))
        {
            unicodeInput = unicodeInput.Substring(1);
        }

        int codePoint = int.Parse(unicodeInput, System.Globalization.NumberStyles.HexNumber);
        char character = (char)codePoint;

        return character.ToString();
    }

    private void PopulateFileExtensionDefinitions(JsonObject iconData)
    {
        // Edit 'file-icons-icon-theme.json' to change the icon definition.
        // Note that there are multiple "fileExtensions" sections in the JSON document, the section
        // you want to edit starts around line 11855.
        // Original json file available here: https://github.com/file-icons/vscode/tree/master/icons

        var fileExtensions = iconData["fileExtensions"] as JsonObject;
        Guard.IsNotNull(fileExtensions);

        foreach (var kv in fileExtensions)
        {
            Guard.IsNotNull(kv.Value);

            string extension = kv.Key;
            string iconName = kv.Value.ToString();

            _fileExtensionDefinitions.Add(extension, iconName);
        }
    }

    private Result<JsonObject> LoadIconData()
    {
        var loadResult = LoadIconDataResource(FileIconsThemeResource);
        if (loadResult.IsFailure)
        {
            return Result<JsonObject>.Fail($"Failed to load icon data from resource '{FileIconsThemeResource}'. Error: {loadResult.DiagnosticReport}");
        }
        var stream = loadResult.Value;

        try
        {
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var jo = JsonNode.Parse(json) as JsonObject;
                if (jo is null)
                {
                    return Result<JsonObject>.Fail("Failed to parse icon data as JSON object.");
                }

                return Result<JsonObject>.Ok(jo);
            }
        }
        catch (Exception ex)
        {
            return Result<JsonObject>.Fail($"An exception occurred when loading the icon data.")
                .WithException(ex);
        }
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
