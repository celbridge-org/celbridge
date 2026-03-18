using System.Text;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Settings;

namespace Celbridge.Extensions;

/// <summary>
/// Discovers extensions and populates the extension registry.
/// Provides document type information and serves template content.
/// </summary>
public class ExtensionService : IExtensionService
{
    private const string ExtensionsFolderName = "extensions";
    private const string ManifestFileName = "extension.toml";

    private readonly ILogger<ExtensionService> _logger;
    private readonly IModuleService _moduleService;
    private readonly IMessengerService _messengerService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IExtensionLocalizationService _localizationService;

    private readonly ExtensionRegistry _registry = new();

    public ExtensionService(
        ILogger<ExtensionService> logger,
        IModuleService moduleService,
        IMessengerService messengerService,
        IFeatureFlags featureFlags,
        IExtensionLocalizationService localizationService)
    {
        _logger = logger;
        _moduleService = moduleService;
        _messengerService = messengerService;
        _featureFlags = featureFlags;
        _localizationService = localizationService;
    }

    public void RegisterExtensions(string projectFolderPath)
    {
        _registry.Clear();

        DiscoverBundledExtensions();
        DiscoverProjectExtensions(projectFolderPath);

        _messengerService.Send(new ExtensionsInitializedMessage());
    }

    public IReadOnlyList<Extension> GetAllExtensions()
    {
        return _registry.GetAllExtensions();
    }

    public IReadOnlyList<DocumentContribution> GetAllDocumentEditors()
    {
        return _registry.GetAllDocumentEditors();
    }

    public IReadOnlyList<DocumentTypeInfo> GetDocumentTypes()
    {
        var contributions = _registry.GetAllDocumentEditors();
        var documentTypes = new List<DocumentTypeInfo>();

        foreach (var contribution in contributions)
        {
            if (contribution.Templates.Count == 0)
            {
                continue;
            }

            var featureFlag = contribution.Extension.FeatureFlag;
            if (!string.IsNullOrEmpty(featureFlag) && !_featureFlags.IsEnabled(featureFlag))
            {
                continue;
            }

            var localizationStrings = _localizationService.LoadStrings(contribution.Extension.ExtensionFolder);
            var displayName = ResolveDisplayName(contribution, localizationStrings);
            var fileExtensions = contribution.FileTypes.Select(ft => ft.FileExtension).ToList().AsReadOnly();

            var documentTypeInfo = new DocumentTypeInfo(displayName, fileExtensions);
            documentTypes.Add(documentTypeInfo);
        }

        return documentTypes.AsReadOnly();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        var normalizedExtension = fileExtension.ToLowerInvariant();
        var contributions = _registry.GetAllDocumentEditors();

        foreach (var contribution in contributions)
        {
            var handlesExtension = contribution.FileTypes
                .Any(ft => ft.FileExtension.Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase));

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

            var templatePath = Path.Combine(contribution.Extension.ExtensionFolder, defaultTemplate.TemplateFile);
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
            catch (Exception exception)
            {
                _logger.LogWarning($"Failed to read template file: {templatePath}. {exception.Message}");
                continue;
            }
        }

        return null;
    }

    private void DiscoverBundledExtensions()
    {
        var extensionFolders = _moduleService.GetBundledExtensionFolders();
        foreach (var extensionFolder in extensionFolders)
        {
            var extension = TryLoadExtension(extensionFolder);
            if (extension is not null)
            {
                _registry.AddBundledExtension(extension);
            }
        }
    }

    private void DiscoverProjectExtensions(string projectFolderPath)
    {
        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return;
        }

        var extensionsFolder = Path.Combine(projectFolderPath, ExtensionsFolderName);

        if (!Directory.Exists(extensionsFolder))
        {
            return;
        }

        var extensionFolders = Directory.GetDirectories(extensionsFolder);
        foreach (var extensionFolder in extensionFolders)
        {
            var extension = TryLoadExtension(extensionFolder);
            if (extension is not null)
            {
                _registry.AddProjectExtension(extension);
            }
        }
    }

    /// <summary>
    /// Attempts to load an extension from a folder. Returns null on failure.
    /// </summary>
    private Extension? TryLoadExtension(string extensionFolder)
    {
        var manifestPath = Path.Combine(extensionFolder, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var loadResult = ManifestLoader.LoadExtension(manifestPath);

        if (loadResult.IsFailure)
        {
            _logger.LogWarning(loadResult, $"Skipping invalid extension: {manifestPath}");
            return null;
        }

        var extension = loadResult.Value;
        foreach (var documentEditor in extension.DocumentEditors)
        {
            var contributionType = documentEditor.GetType().Name;
            _logger.LogDebug($"Discovered extension document: {documentEditor.Id} ({contributionType}) for {string.Join(", ", documentEditor.FileTypes.Select(ft => ft.FileExtension))}");
        }

        return extension;
    }

    /// <summary>
    /// Resolves the display name for a contribution.
    /// Uses the first file type's display name if available, looked up in the localization dictionary.
    /// Falls back to the extension name.
    /// </summary>
    private static string ResolveDisplayName(
        DocumentContribution contribution,
        IReadOnlyDictionary<string, string> localizationStrings)
    {
        var firstFileType = contribution.FileTypes.FirstOrDefault();
        if (firstFileType is not null && !string.IsNullOrEmpty(firstFileType.DisplayName))
        {
            if (localizationStrings.TryGetValue(firstFileType.DisplayName, out var localizedName))
            {
                return localizedName;
            }
            return firstFileType.DisplayName;
        }
        return contribution.Extension.Name;
    }
}
