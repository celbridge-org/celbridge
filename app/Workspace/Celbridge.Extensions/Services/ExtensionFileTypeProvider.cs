using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Extensions;

/// <summary>
/// Provides file type information and template content from discovered extension manifests.
/// Bridges the ExtensionDiscoveryService to consumers in
/// other projects via the IExtensionFileTypeProvider abstraction (Foundation).
/// </summary>
public class ExtensionFileTypeProvider : IExtensionFileTypeProvider
{
    private readonly ILogger<ExtensionFileTypeProvider> _logger;
    private readonly ExtensionRegistry _extensionRegistry;
    private readonly IProjectService _projectService;

    public ExtensionFileTypeProvider(
        ILogger<ExtensionFileTypeProvider> logger,
        ExtensionRegistry extensionRegistry,
        IProjectService projectService)
    {
        _logger = logger;
        _extensionRegistry = extensionRegistry;
        _projectService = projectService;
    }

    public IReadOnlyList<ExtensionFileTypeInfo> GetExtensionFileTypes()
    {
        var manifests = DiscoverManifests();
        var fileTypes = new List<ExtensionFileTypeInfo>();

        foreach (var manifest in manifests)
        {
            // Only include extensions that declare templates (these are "file types" users can create)
            if (manifest.Templates.Count == 0)
            {
                continue;
            }

            var locStrings = LoadLocalizationStrings(manifest);
            foreach (var fileType in manifest.FileTypes)
            {
                var displayName = ResolveFileTypeDisplayName(fileType, locStrings, manifest.Name);
                var info = new ExtensionFileTypeInfo(
                    displayName,
                    fileType.Extension,
                    manifest.FeatureFlag);
                fileTypes.Add(info);
            }
        }

        return fileTypes.AsReadOnly();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        var manifests = DiscoverManifests();

        foreach (var manifest in manifests)
        {
            var handlesExtension = manifest.FileTypes
                .Any(ft => ft.Extension.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase));

            if (!handlesExtension)
            {
                continue;
            }

            var defaultTemplate = manifest.Templates
                .FirstOrDefault(t => t.Default);

            if (defaultTemplate is null)
            {
                continue;
            }

            var templatePath = Path.Combine(manifest.ExtensionDirectory, defaultTemplate.File);
            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template file not found: {templatePath}");
                continue;
            }

            try
            {
                var content = File.ReadAllText(templatePath, Encoding.UTF8);
                return Encoding.UTF8.GetBytes(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read template file: {templatePath}. {ex.Message}");
                continue;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> LoadLocalizationStrings(ExtensionManifest manifest)
    {
        if (!string.IsNullOrEmpty(manifest.Localization))
        {
            return ExtensionLocalizationHelper.LoadStrings(
                manifest.ExtensionDirectory,
                manifest.Localization);
        }
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Resolves the display name for a single file type entry.
    /// If the file type declares a displayName, it is looked up in the localization dictionary.
    /// If not found in localization, the raw displayName value is used as a literal string.
    /// If displayName is empty, falls back to the manifest name.
    /// </summary>
    private static string ResolveFileTypeDisplayName(
        ExtensionFileType fileType,
        IReadOnlyDictionary<string, string> locStrings,
        string fallbackName)
    {
        if (!string.IsNullOrEmpty(fileType.DisplayName))
        {
            if (locStrings.TryGetValue(fileType.DisplayName, out var localizedName))
            {
                return localizedName;
            }
            return fileType.DisplayName;
        }
        return fallbackName;
    }

    private IReadOnlyList<ExtensionManifest> DiscoverManifests()
    {
        var projectFolderPath = _projectService.CurrentProject?.ProjectFolderPath ?? string.Empty;
        return _extensionRegistry.DiscoverExtensions(projectFolderPath);
    }
}
