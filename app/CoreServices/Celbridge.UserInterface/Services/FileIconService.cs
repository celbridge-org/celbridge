using Newtonsoft.Json.Linq;
using System.Reflection;

namespace Celbridge.UserInterface.Services;

public class FileIconService : IFileIconService
{
    private const string DefaultFileIconName = "_file";
    private const string DefaultFolderIconName = "_folder";
    private const string DefaultColor = "#9dc0ce";

    private const string FileIconsThemeResource = "Assets.Fonts.FileIcons.file-icons-icon-theme.json";

    private Dictionary<string, string> _fileExtensionDefinitions = new();
    private Dictionary<string, FileIconDefinition> _iconDefinitions = new();

    public FileIconDefinition DefaultFileIcon { get; private set; }
    public FileIconDefinition DefaultFolderIcon { get; private set; }

    public FileIconService()
    {
        var loadResult = LoadDefinitions();
        if (loadResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to load file icon definitions. {loadResult.Error}");
        }

        var getFileResult = GetFileIcon(DefaultFileIconName);
        if (getFileResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to get default file icon definitions. {getFileResult.Error}");
        }
        DefaultFileIcon = getFileResult.Value;

        var getFolderResult = GetFileIcon(DefaultFolderIconName);
        if (getFolderResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to get default folder icon definitions. {getFolderResult.Error}");
        }
        DefaultFolderIcon = getFolderResult.Value;
    }

    public Result LoadDefinitions()
    {
        var loadResult = LoadIconData();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load file icon definition. {loadResult.Error}");
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

    private void PopulateFileExtensionDefinitions(JObject iconData)
    {
        // Edit 'file-icons-icon-theme.json' to change the icon definition.
        // Note that there are multiple "fileExtensions" sections in the JSON document, the section
        // you want to edit starts around line 11855.
        // Original json file available here: https://github.com/file-icons/vscode/tree/master/icons

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
        var loadResult = LoadIconDataResource(FileIconsThemeResource);
        if (loadResult.IsFailure)
        {
            return Result<JObject>.Fail($"Failed to load icon data from resource '{FileIconsThemeResource}'. Error: {loadResult.Error}");
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
        catch (Exception ex)
        {
            return Result<JObject>.Fail($"An exception occurred when loading the icon data.")
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
