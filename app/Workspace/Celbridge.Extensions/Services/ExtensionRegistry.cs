using Celbridge.Logging;

namespace Celbridge.Extensions;

/// <summary>
/// Service that discovers document contributions from a project's extensions directory
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
    /// Discovers all document contributions from both the project's extensions directory
    /// and registered bundled extension paths.
    /// Returns an empty list if no valid contributions are found.
    /// </summary>
    public IReadOnlyList<DocumentContribution> DiscoverExtensions(string projectFolderPath)
    {
        var contributions = new List<DocumentContribution>();

        // Scan the project's extensions directory
        var projectContributions = DiscoverProjectExtensions(projectFolderPath);
        contributions.AddRange(projectContributions);

        // Scan registered bundled extension paths
        var bundledContributions = DiscoverBundledExtensions();
        contributions.AddRange(bundledContributions);

        return contributions.AsReadOnly();
    }

    /// <summary>
    /// Discovers document contributions from the project's extensions directory.
    /// </summary>
    private List<DocumentContribution> DiscoverProjectExtensions(string projectFolderPath)
    {
        var extensionsFolder = Path.Combine(projectFolderPath, ExtensionsFolderName);

        if (!Directory.Exists(extensionsFolder))
        {
            return [];
        }

        var contributions = new List<DocumentContribution>();

        // Scan each subdirectory for an extension.toml manifest
        var extensionDirs = Directory.GetDirectories(extensionsFolder);
        foreach (var extensionDir in extensionDirs)
        {
            var loaded = TryLoadExtension(extensionDir);
            contributions.AddRange(loaded);
        }

        return contributions;
    }

    /// <summary>
    /// Discovers document contributions from registered bundled extension paths.
    /// </summary>
    private List<DocumentContribution> DiscoverBundledExtensions()
    {
        var contributions = new List<DocumentContribution>();

        foreach (var extensionDir in _bundledExtensionPaths)
        {
            var loaded = TryLoadExtension(extensionDir);
            contributions.AddRange(loaded);
        }

        return contributions;
    }

    /// <summary>
    /// Attempts to load all document contributions from an extension directory.
    /// Returns an empty list on failure.
    /// </summary>
    private List<DocumentContribution> TryLoadExtension(string extensionDir)
    {
        var manifestPath = Path.Combine(extensionDir, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var loadResult = ManifestLoader.LoadExtension(manifestPath);

        if (loadResult.IsFailure)
        {
            _logger.LogWarning(loadResult, $"Skipping invalid extension: {manifestPath}");
            return [];
        }

        var contributions = loadResult.Value;
        foreach (var contribution in contributions)
        {
            _logger.LogDebug($"Discovered extension document: {contribution.Id} ({contribution.Type}) for {string.Join(", ", contribution.FileTypes.Select(ft => ft.Extension))}");
        }

        return contributions.ToList();
    }
}
