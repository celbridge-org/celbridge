using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Settings;

namespace Celbridge.Extensions;

/// <summary>
/// Thin facade over ExtensionRegistry that triggers discovery and sends notifications.
/// </summary>
public class ExtensionService : IExtensionService
{
    private readonly IMessengerService _messengerService;
    private readonly ExtensionRegistry _registry;

    public ExtensionService(
        ILogger<ExtensionRegistry> logger,
        IModuleService moduleService,
        IMessengerService messengerService,
        IFeatureFlags featureFlags,
        IExtensionLocalizationService localizationService)
    {
        _messengerService = messengerService;
        _registry = new ExtensionRegistry(logger, moduleService, featureFlags, localizationService);
    }

    public void RegisterExtensions(string projectFolderPath)
    {
        _registry.DiscoverExtensions(projectFolderPath);
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
        return _registry.GetDocumentTypes();
    }

    public byte[]? GetDefaultTemplateContent(string fileExtension)
    {
        return _registry.GetDefaultTemplateContent(fileExtension);
    }
}
