using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace Celbridge.UserInterface.Services;

public class IconService : IIconService
{
    private const string DefaultFileIconName = "_file";
    private const string DefaultFolderIconName = "_folder";
    private const string DefaultColor = "#9dc0ce";

    private Dictionary<string, string> _fileExtensionDefinitions = new();
    private Dictionary<string, IconDefinition> _iconDefinitions = new();

    public IconDefinition DefaultFileIcon { get; private set; }
    public IconDefinition DefaultFolderIcon { get; private set; }

    public IconService()
    {
        var loadResult = LoadIconDefinitions();
        if (loadResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load icon definitions. {loadResult.Error}");
        }

        var getFileResult = GetIcon(DefaultFileIconName);
        if (getFileResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to get default file icon definitions. {getFileResult.Error}");
        }
        DefaultFileIcon = getFileResult.Value;

        var getFolderResult = GetIcon(DefaultFolderIconName);
        if (getFolderResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to get default folder icon definitions. {getFolderResult.Error}");
        }
        DefaultFolderIcon = getFolderResult.Value;
    }

    public Result LoadIconDefinitions()
    {
        var loadResult = LoadIconData();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load icon definition. {loadResult.Error}");
        }
        var iconData = loadResult.Value;

        try
        {
            PopulateIconDefinitions(iconData);
            PopulateFileExtensionDefinitions(iconData);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load icon definitions. {ex.Message}");
        }

        return Result.Ok();
    }

    public Result<IconDefinition> GetIcon(string iconName)
    {
        if (!_iconDefinitions.TryGetValue(iconName, out IconDefinition? iconDefinition))
        {
            if (_iconDefinitions.TryGetValue(DefaultFileIconName, out IconDefinition? defaultIcon))
            {
                // Icon definition not found, return default icon
                return Result<IconDefinition>.Ok(defaultIcon);
            }

            return Result<IconDefinition>.Fail($"No default icon found.");
        }

        return Result<IconDefinition>.Ok(iconDefinition);
    }

    public Result<IconDefinition> GetIconForFileExtension(string fileExtension)
    {
        if (!_fileExtensionDefinitions.TryGetValue(fileExtension, out string? iconName))
        {
            if (_iconDefinitions.TryGetValue(DefaultFileIconName, out IconDefinition? defaultIcon))
            {
                // File extension not recognized, return default icon
                return Result<IconDefinition>.Ok(defaultIcon);
            }

            return Result<IconDefinition>.Fail($"No default icon found.");
        }

        return GetIcon(iconName);
    }

    public IconDefinition GetDefaultFileIcon()
    {
        if (_iconDefinitions.TryGetValue(DefaultFileIconName, out IconDefinition? defaultIcon))
        {
            return defaultIcon;
        }

        throw new InvalidOperationException();
    }

    private void PopulateIconDefinitions(JObject iconData)
    {
        var iconDefinitions = iconData["iconDefinitions"] as JObject;
        Guard.IsNotNull(iconDefinitions);

        foreach (var kv in iconDefinitions)
        {
            Guard.IsNotNull(kv.Value);

            string iconName = kv.Key;
            var iconProperties = kv.Value as JObject;
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

            var iconDefinition = new IconDefinition(fontCharacter, color, fontFamily, fontSize);

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

    private void PopulateFileExtensionDefinitions(JObject iconData)
    {
        var fileExtensions = iconData["fileExtensions"] as JObject;
        Guard.IsNotNull(fileExtensions);

        foreach (var kv in fileExtensions)
        {
            Guard.IsNotNull(kv.Value);

            string extension = kv.Key;
            string iconName = kv.Value.ToString();

            _fileExtensionDefinitions.Add(extension, iconName);
        }
    }

    private Result<JObject> LoadIconData()
    {
        var resourceName = "Assets.file-icons-icon-theme.json";
        var loadResult = LoadIconDataResource(resourceName);
        if (loadResult.IsFailure)
        {
            return Result<JObject>.Fail($"Failed to load icon data from resource '{resourceName}'. Error: {loadResult.Error}");
        }
        var stream = loadResult.Value;

        try
        {
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var jo = JObject.Parse(json);

                return Result<JObject>.Ok(jo);
            }
        }
        catch (JsonReaderException jex)
        {
            return Result<JObject>.Fail($"JSON Parsing Error while loading icon data: {jex.Message}");
        }
        catch (IOException ioex)
        {
            return Result<JObject>.Fail($"IO Error while reading icon data: {ioex.Message}");
        }
        catch (Exception ex)
        {
            return Result<JObject>.Fail($"Unexpected error while loading icon data: {ex.Message}");
        }
    }

    private Result<Stream> LoadIconDataResource(string searchResourceName)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        Guard.IsNotNull(entryAssembly);

        // The name is prepended with the namespace which includes the platform.
        // e.g. On Windows the name starts with "Celbridge.Windows." on Windows.
        // To work around this we look for a resource that ends with the requested resource name.

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
