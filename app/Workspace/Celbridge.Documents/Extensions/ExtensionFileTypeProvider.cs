using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Documents.Extensions;

/// <summary>
/// Provides file type information and template content from discovered extension manifests.
/// Bridges the ExtensionDiscoveryService (Documents project) to consumers in
/// other projects via the IExtensionFileTypeProvider abstraction (Foundation).
/// </summary>
public class ExtensionFileTypeProvider : IExtensionFileTypeProvider
{
    private readonly ILogger<ExtensionFileTypeProvider> _logger;
    private readonly ExtensionDiscoveryService _discoveryService;
    private readonly IProjectService _projectService;

    public ExtensionFileTypeProvider(
        ILogger<ExtensionFileTypeProvider> logger,
        ExtensionDiscoveryService discoveryService,
        IProjectService projectService)
    {
        _logger = logger;
        _discoveryService = discoveryService;
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

            var displayName = ResolveDisplayName(manifest);
            var extension = manifest.Extensions.First();

            var info = new ExtensionFileTypeInfo(
                displayName,
                extension,
                manifest.FeatureFlag);
            fileTypes.Add(info);
        }

        return fileTypes.AsReadOnly();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        var manifests = DiscoverManifests();

        foreach (var manifest in manifests)
        {
            var handlesExtension = manifest.Extensions
                .Any(e => e.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Resolves a human-readable display name for the extension.
    /// If the manifest declares a localization folder, tries to look up "AddFileDialog_FileType_{Name}"
    /// from the extension's localization file. Falls back to the manifest name.
    /// </summary>
    private static string ResolveDisplayName(ExtensionManifest manifest)
    {
        if (!string.IsNullOrEmpty(manifest.Localization))
        {
            var strings = ExtensionLocalizationHelper.LoadStrings(
                manifest.ExtensionDirectory,
                manifest.Localization);

            var localizationKey = $"AddFileDialog_FileType_{manifest.Name}";
            if (strings.TryGetValue(localizationKey, out var localizedName))
            {
                return localizedName;
            }
        }

        // Fall back to manifest name
        return manifest.Name;
    }

    private IReadOnlyList<ExtensionManifest> DiscoverManifests()
    {
        var projectFolderPath = _projectService.CurrentProject?.ProjectFolderPath ?? string.Empty;
        return _discoveryService.DiscoverExtensions(projectFolderPath);
    }
}
