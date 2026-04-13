using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Settings;

namespace Celbridge.Packages;

/// <summary>
/// Thin facade over PackageRegistry that triggers discovery and sends notifications.
/// </summary>
public class PackageService : IPackageService
{
    private readonly IMessengerService _messengerService;
    private readonly PackageRegistry _registry;

    public PackageService(
        ILogger<PackageRegistry> logger,
        IModuleService moduleService,
        IMessengerService messengerService,
        IFeatureFlags featureFlags,
        IPackageLocalizationService localizationService)
    {
        _messengerService = messengerService;
        _registry = new PackageRegistry(logger, moduleService, featureFlags, localizationService);
    }

    public void RegisterPackages(string projectFolderPath)
    {
        _registry.DiscoverPackages(projectFolderPath);
        _messengerService.Send(new PackagesInitializedMessage());
    }

    public IReadOnlyList<Package> GetAllPackages()
    {
        return _registry.GetAllPackages();
    }

    public IReadOnlyList<DocumentEditorContribution> GetAllDocumentEditors()
    {
        return _registry.GetAllDocumentEditors();
    }

    public IReadOnlyList<DocumentTypeInfo> GetDocumentTypes()
    {
        return _registry.GetDocumentTypes();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        return _registry.GetDefaultTemplateContent(fileExtension);
    }
}
