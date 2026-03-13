using Celbridge.Logging;

namespace Celbridge.Extensions;

/// <summary>
/// Service that discovers extension manifests from a project's extensions directory
/// and from registered bundled extension paths.
/// Parses extension.toml manifests and their referenced document contribution files.
/// </summary>
public class ExtensionRegistry
{
    private const string ExtensionsFolderName = "extensions";
    private const string ManifestFileName = "extension.toml";

    private readonly ILogger<ExtensionRegistry> _logger;
    private readonly List<string> _bundledExtensionPaths = [];

    public ExtensionRegistry(ILogger<ExtensionRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a bundled extension directory path.
    /// The path should point to a directory containing an extension.toml manifest.
    /// </summary>
    public void RegisterBundledExtensionPath(string path)
    {
        if (!_bundledExtensionPaths.Contains(path))
        {
            _bundledExtensionPaths.Add(path);
        }
    }

    /// <summary>
    /// Gets all registered bundled extension paths.
    /// </summary>
    public IReadOnlyList<string> BundledExtensionPaths => _bundledExtensionPaths.AsReadOnly();

    /// <summary>
    /// Discovers all extension manifests from both the project's extensions directory
    /// and registered bundled extension paths.
    /// Returns an empty list if no valid manifests are found.
    /// </summary>
    public IReadOnlyList<ExtensionManifest> DiscoverExtensions(string projectFolderPath)
    {
        var manifests = new List<ExtensionManifest>();

        // Scan the project's extensions directory
        var projectManifests = DiscoverProjectExtensions(projectFolderPath);
        manifests.AddRange(projectManifests);

        // Scan registered bundled extension paths
        var bundledManifests = DiscoverBundledExtensions();
        manifests.AddRange(bundledManifests);

        return manifests.AsReadOnly();
    }

    /// <summary>
    /// Discovers extension manifests from the project's extensions directory.
    /// </summary>
    private List<ExtensionManifest> DiscoverProjectExtensions(string projectFolderPath)
    {
        var extensionsFolder = Path.Combine(projectFolderPath, ExtensionsFolderName);

        if (!Directory.Exists(extensionsFolder))
        {
            return [];
        }

        var manifests = new List<ExtensionManifest>();

        // Scan each subdirectory for an extension.toml manifest
        var extensionDirs = Directory.GetDirectories(extensionsFolder);
        foreach (var extensionDir in extensionDirs)
        {
            var loaded = TryLoadExtension(extensionDir);
            manifests.AddRange(loaded);
        }

        return manifests;
    }

    /// <summary>
    /// Discovers extension manifests from registered bundled extension paths.
    /// </summary>
    private List<ExtensionManifest> DiscoverBundledExtensions()
    {
        var manifests = new List<ExtensionManifest>();

        foreach (var extensionDir in _bundledExtensionPaths)
        {
            var loaded = TryLoadExtension(extensionDir);
            manifests.AddRange(loaded);
        }

        return manifests;
    }

    /// <summary>
    /// Attempts to load all document manifests from an extension directory.
    /// Returns an empty list on failure.
    /// </summary>
    private List<ExtensionManifest> TryLoadExtension(string extensionDir)
    {
        var manifestPath = Path.Combine(extensionDir, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var loadResult = ExtensionLoader.LoadExtension(manifestPath);

        if (loadResult.IsFailure)
        {
            _logger.LogWarning(loadResult, $"Skipping invalid extension: {manifestPath}");
            return [];
        }

        var manifests = loadResult.Value;
        foreach (var manifest in manifests)
        {
            _logger.LogDebug($"Discovered extension document: {manifest.Id} ({manifest.Type}) for {string.Join(", ", manifest.FileTypes.Select(ft => ft.Extension))}");
        }

        return manifests.ToList();
    }
}
