using System.Globalization;
using System.Text.Json;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Packages;

/// <summary>
/// Loads localized strings from a package's localization folder.
/// Uses convention: packages store localization files in a "localization" subfolder
/// as flat key-value JSON dictionaries (e.g., en.json, fr.json).
/// </summary>
public class PackageLocalizationService : IPackageLocalizationService
{
    /// <summary>
    /// Convention: all packages use "localization" as the folder name.
    /// </summary>
    public const string LocalizationFolder = "localization";

    private const string FallbackLocale = "en";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<PackageLocalizationService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IPackageReader _bundledReader;

    public PackageLocalizationService(
        ILogger<PackageLocalizationService> logger,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
        _bundledReader = new DirectPackageReader(fileSystem);
    }

    public Dictionary<string, string> LoadStrings(PackageInfo package, string? locale = null)
    {
        locale ??= CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var reader = GetReaderForPackage(package);
        var localizationFolder = Path.Combine(package.PackageFolder, LocalizationFolder);

        var localePath = Path.Combine(localizationFolder, $"{locale}.json");
        var result = TryLoadJsonFile(reader, localePath);
        if (result is not null)
        {
            return result;
        }

        if (locale != FallbackLocale)
        {
            var fallbackPath = Path.Combine(localizationFolder, $"{FallbackLocale}.json");
            result = TryLoadJsonFile(reader, fallbackPath);
            if (result is not null)
            {
                return result;
            }
        }

        return new Dictionary<string, string>();
    }

    // Project packages route through the gateway by reverse-resolving the path
    // to a ResourceKey; bundled packages stay on direct File.* IO. The project
    // reader is constructed on demand because the workspace-scoped IResourceFileSystem
    // and IResourceRegistry must be looked up at call time.
    private IPackageReader GetReaderForPackage(PackageInfo package)
    {
        if (package.Origin == PackageOrigin.Project)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            return new ResourceFileSystemPackageReader(resourceFileSystem, resourceRegistry);
        }

        return _bundledReader;
    }

    private Dictionary<string, string>? TryLoadJsonFile(IPackageReader reader, string path)
    {
        if (!reader.Exists(path))
        {
            return null;
        }

        var readResult = reader.ReadAllText(path);
        if (readResult.IsFailure)
        {
            _logger.LogWarning($"Failed to load localization file: {path}. {readResult.FirstErrorMessage}");
            return null;
        }

        try
        {
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(readResult.Value, _jsonOptions);
            return dictionary;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, $"Failed to parse localization file: {path}");
            return null;
        }
    }
}
