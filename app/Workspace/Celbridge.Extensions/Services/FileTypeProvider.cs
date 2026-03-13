using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Extensions;

/// <summary>
/// Provides file type information and template content from discovered document contributions.
/// Bridges the ExtensionDiscoveryService to consumers in
/// other projects via the IExtensionFileTypeProvider abstraction (Foundation).
/// </summary>
public class FileTypeProvider : IExtensionFileTypeProvider
{
    private readonly ILogger<FileTypeProvider> _logger;
    private readonly ExtensionRegistry _extensionRegistry;
    private readonly IProjectService _projectService;

    public FileTypeProvider(
        ILogger<FileTypeProvider> logger,
        ExtensionRegistry extensionRegistry,
        IProjectService projectService)
    {
        _logger = logger;
        _extensionRegistry = extensionRegistry;
        _projectService = projectService;
    }

    public IReadOnlyList<ExtensionFileTypeInfo> GetExtensionFileTypes()
    {
        var contributions = DiscoverDocumentContributions();
        var fileTypes = new List<ExtensionFileTypeInfo>();

        foreach (var contribution in contributions)
        {
            // Only include contributions that declare templates (these are "file types" users can create)
            if (contribution.Templates.Count == 0)
            {
                continue;
            }

            var locStrings = LoadLocalizationStrings(contribution);
            foreach (var fileType in contribution.FileTypes)
            {
                var displayName = ResolveFileTypeDisplayName(fileType, locStrings, contribution.Extension.Name);
                var info = new ExtensionFileTypeInfo(
                    displayName,
                    fileType.Extension,
                    contribution.Extension.FeatureFlag);
                fileTypes.Add(info);
            }
        }

        return fileTypes.AsReadOnly();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        var contributions = DiscoverDocumentContributions();

        foreach (var contribution in contributions)
        {
            var handlesExtension = contribution.FileTypes
                .Any(ft => ft.Extension.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase));

            if (!handlesExtension)
            {
                continue;
            }

            var defaultTemplate = contribution.Templates
                .FirstOrDefault(t => t.Default);

            if (defaultTemplate is null)
            {
                continue;
            }

            var templatePath = Path.Combine(contribution.Extension.ExtensionDirectory, defaultTemplate.File);
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
    /// Loads localization strings from the extension's localization directory.
    /// Uses convention: {extensionDirectory}/localization/{locale}.json
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadLocalizationStrings(DocumentContribution contribution)
    {
        return LocalizationHelper.LoadStrings(contribution.Extension.ExtensionDirectory);
    }

    /// <summary>
    /// Resolves the display name for a single file type entry.
    /// If the file type declares a displayName, it is looked up in the localization dictionary.
    /// If not found in localization, the raw displayName value is used as a literal string.
    /// If displayName is empty, falls back to the extension name.
    /// </summary>
    private static string ResolveFileTypeDisplayName(
        DocumentFileType fileType,
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

    private IReadOnlyList<DocumentContribution> DiscoverDocumentContributions()
    {
        var projectFolderPath = _projectService.CurrentProject?.ProjectFolderPath ?? string.Empty;
        return _extensionRegistry.DiscoverExtensions(projectFolderPath);
    }
}
