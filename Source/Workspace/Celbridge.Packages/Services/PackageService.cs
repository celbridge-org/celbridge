using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Messaging;

namespace Celbridge.Packages;

/// <summary>
/// Thin facade over PackageRegistry that triggers discovery and sends notifications.
/// </summary>
public class PackageService : IPackageService
{
    private readonly IMessengerService _messengerService;
    private readonly PackageRegistry _registry;

    public PackageService(
        IMessengerService messengerService,
        PackageRegistry registry)
    {
        _messengerService = messengerService;
        _registry = registry;
    }

    public async Task RegisterPackagesAsync(string projectFolderPath)
    {
        var report = await _registry.DiscoverPackagesAsync(projectFolderPath);

        if (report.Failures.Count > 0)
        {
            // Surface the failures via the console panel error banner.
            // Individual failures are already logged by the registry.
            var projectName = Path.GetFileName(projectFolderPath) ?? string.Empty;
            var message = new ConsoleErrorMessage(ConsoleErrorType.PackageLoadError, projectName);
            _messengerService.Send(message);
        }

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

    public Package? GetContributingPackage(DocumentEditorId editorId)
    {
        return _registry.GetContributingPackage(editorId);
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
