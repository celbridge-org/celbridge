using Celbridge.Logging;

namespace Celbridge.Documents.Extensions;

/// <summary>
/// Service that discovers extension editor manifests from a project's extensions directory.
/// Scans for editor.json files in subdirectories of the extensions/ folder.
/// </summary>
public class ExtensionDiscoveryService
{
    private const string ExtensionsFolderName = "extensions";
    private const string ManifestFileName = "editor.json";

    private readonly ILogger<ExtensionDiscoveryService> _logger;

    public ExtensionDiscoveryService(ILogger<ExtensionDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovers all extension manifests in the project's extensions directory.
    /// Returns an empty list if the extensions directory does not exist or contains no valid manifests.
    /// </summary>
    public IReadOnlyList<ExtensionManifest> DiscoverExtensions(string projectFolderPath)
    {
        var extensionsFolder = Path.Combine(projectFolderPath, ExtensionsFolderName);

        if (!Directory.Exists(extensionsFolder))
        {
            return [];
        }

        var manifests = new List<ExtensionManifest>();

        // Scan each subdirectory for an editor.json manifest
        var extensionDirs = Directory.GetDirectories(extensionsFolder);
        foreach (var extensionDir in extensionDirs)
        {
            var manifestPath = Path.Combine(extensionDir, ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var parseResult = ExtensionManifest.Parse(manifestPath);

            if (parseResult.IsFailure)
            {
                _logger.LogWarning(parseResult, $"Skipping invalid extension manifest: {manifestPath}");
                continue;
            }

            var manifest = parseResult.Value;
            manifests.Add(manifest);

            _logger.LogDebug($"Discovered extension: {manifest.Name} ({manifest.Type}) for {string.Join(", ", manifest.Extensions)}");
        }

        return manifests.AsReadOnly();
    }
}
