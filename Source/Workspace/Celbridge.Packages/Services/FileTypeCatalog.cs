using System.Text.Json;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Platform;

namespace Celbridge.Packages;

/// <summary>
/// One catalogued file type: the language a code editor highlights it as, the categories it is grouped
/// under, and the name it is known by. Every field is optional.
/// </summary>
internal sealed record FileTypeEntry(
    string Language,
    IReadOnlyList<FileTypeCategory> Categories,
    string DisplayName,
    FileTypeIcon? Icon);

public sealed class FileTypeCatalog : IFileTypeCatalog
{
    private const string CatalogRelativePath = "celbridge-client/file-types.json";

    private const string LanguageKey = "language";
    private const string CategoriesKey = "categories";
    private const string DisplayNameKey = "display-name";
    private const string IconKey = "icon";
    private const string IconColorKey = "icon-color";
    private const string IconScaleKey = "icon-scale";

    private static readonly IReadOnlyList<FileTypeCategory> NoCategories = Array.Empty<FileTypeCategory>();

    private readonly ILogger<FileTypeCatalog> _logger;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IAppEnvironment _appEnvironment;

    private readonly Dictionary<string, FileTypeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileTypeEntry> _fileNameEntries = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _languageExtensions = new();
    private List<string> _iconExtensions = new();
    private List<string> _iconFileNames = new();

    private bool _loaded;

    public FileTypeCatalog(
        ILogger<FileTypeCatalog> logger,
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _appEnvironment = appEnvironment;
    }

    public IReadOnlyList<string> LanguageExtensions => _languageExtensions;

    public IReadOnlyList<string> IconExtensions => _iconExtensions;

    public IReadOnlyList<string> IconFileNames => _iconFileNames;

    // A catalog that fails to load leaves every extension uncatalogued rather than stopping the
    // application. The code editor then claims no file types and its package reports a load failure.
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;

        var catalogPath = Path.Combine(_appEnvironment.SharedWebAssetsFolderPath, CatalogRelativePath);

        var readResult = await _fileSystem.ReadAllTextAsync(catalogPath);
        if (readResult.IsFailure)
        {
            _logger.LogError(readResult, $"Failed to read the file type catalog: {catalogPath}");
            return;
        }
        var json = readResult.Value;

        try
        {
            ParseCatalog(json);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, $"Failed to parse the file type catalog: {catalogPath}");
            _entries.Clear();
            _fileNameEntries.Clear();
            _languageExtensions = new List<string>();
            _iconExtensions = new List<string>();
            _iconFileNames = new List<string>();
        }
    }

    public IReadOnlyList<FileTypeCategory> GetCategories(string extension)
    {
        if (TryGetEntry(extension, out var entry))
        {
            return entry.Categories;
        }

        return NoCategories;
    }

    public string GetLanguage(string extension)
    {
        if (TryGetEntry(extension, out var entry))
        {
            return entry.Language;
        }

        return string.Empty;
    }

    public string GetDisplayName(string extension)
    {
        if (TryGetEntry(extension, out var entry))
        {
            return entry.DisplayName;
        }

        return string.Empty;
    }

    public FileTypeIcon? GetIcon(string extension)
    {
        if (TryGetEntry(extension, out var entry))
        {
            return entry.Icon;
        }

        return null;
    }

    public FileTypeIcon? GetIconForFileName(string fileName)
    {
        if (!string.IsNullOrEmpty(fileName) &&
            _fileNameEntries.TryGetValue(fileName, out var entry))
        {
            return entry.Icon;
        }

        return null;
    }

    private bool TryGetEntry(string extension, out FileTypeEntry entry)
    {
        if (string.IsNullOrEmpty(extension))
        {
            entry = null!;
            return false;
        }

        return _entries.TryGetValue(extension, out entry!);
    }

    private void ParseCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The file type catalog must be a JSON object with extension keys.");
        }

        var languageExtensions = new List<string>();
        var iconExtensions = new List<string>();
        var iconFileNames = new List<string>();

        foreach (var property in document.RootElement.EnumerateObject())
        {
            // A key beginning with a dot is an extension; anything else is a whole file name, for the
            // files that carry no usable extension.
            if (!property.Name.StartsWith('.'))
            {
                var fileNameEntry = ParseEntry(property.Value);
                _fileNameEntries[property.Name] = fileNameEntry;

                if (fileNameEntry.Icon is not null)
                {
                    iconFileNames.Add(property.Name);
                }

                continue;
            }

            var extension = property.Name.ToLowerInvariant();
            if (!FileExtensionUtils.IsWellFormedFileExtension(extension))
            {
                _logger.LogWarning($"Skipping malformed extension key in the file type catalog: {property.Name}");
                continue;
            }

            var entry = ParseEntry(property.Value);
            _entries[extension] = entry;

            if (!string.IsNullOrEmpty(entry.Language))
            {
                languageExtensions.Add(extension);
            }

            if (entry.Icon is not null)
            {
                iconExtensions.Add(extension);
            }
        }

        _languageExtensions = languageExtensions;
        _iconExtensions = iconExtensions;
        _iconFileNames = iconFileNames;
    }

    private FileTypeEntry ParseEntry(JsonElement element)
    {
        var language = string.Empty;
        if (element.TryGetProperty(LanguageKey, out var languageElement) &&
            languageElement.ValueKind == JsonValueKind.String)
        {
            language = languageElement.GetString() ?? string.Empty;
        }

        var displayName = string.Empty;
        if (element.TryGetProperty(DisplayNameKey, out var displayNameElement) &&
            displayNameElement.ValueKind == JsonValueKind.String)
        {
            displayName = displayNameElement.GetString() ?? string.Empty;
        }

        FileTypeIcon? icon = null;
        if (element.TryGetProperty(IconKey, out var iconElement) &&
            iconElement.ValueKind == JsonValueKind.String)
        {
            var iconName = iconElement.GetString() ?? string.Empty;
            var iconColor = string.Empty;
            if (element.TryGetProperty(IconColorKey, out var iconColorElement) &&
                iconColorElement.ValueKind == JsonValueKind.String)
            {
                iconColor = iconColorElement.GetString() ?? string.Empty;
            }

            var iconScale = 1.0;
            if (element.TryGetProperty(IconScaleKey, out var iconScaleElement) &&
                iconScaleElement.ValueKind == JsonValueKind.Number)
            {
                iconScale = iconScaleElement.GetDouble();
            }

            icon = new FileTypeIcon(iconName, iconColor, iconScale);
        }

        var categories = NoCategories;
        if (element.TryGetProperty(CategoriesKey, out var categoriesElement) &&
            categoriesElement.ValueKind == JsonValueKind.Array)
        {
            var parsed = new List<FileTypeCategory>();
            foreach (var categoryElement in categoriesElement.EnumerateArray())
            {
                var categoryName = categoryElement.GetString();
                if (Enum.TryParse<FileTypeCategory>(categoryName, ignoreCase: true, out var category))
                {
                    parsed.Add(category);
                }
                else
                {
                    _logger.LogWarning($"Skipping unknown category '{categoryName}' in the file type catalog.");
                }
            }
            categories = parsed;
        }

        return new FileTypeEntry(language, categories, displayName, icon);
    }
}
