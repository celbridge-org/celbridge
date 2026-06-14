using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Messaging;
using Celbridge.Projects;

namespace Celbridge.Packages;

/// <summary>
/// Thin facade over PackageRegistry that triggers discovery and sends notifications.
/// </summary>
public class PackageService : IPackageService
{
    private readonly IMessengerService _messengerService;
    private readonly IProjectLoadReporter _loadReporter;
    private readonly PackageRegistry _registry;

    public PackageService(
        IMessengerService messengerService,
        IProjectLoadReporter loadReporter,
        PackageRegistry registry)
    {
        _messengerService = messengerService;
        _loadReporter = loadReporter;
        _registry = registry;
    }

    public async Task RegisterPackagesAsync(string projectFolderPath)
    {
        var report = await _registry.DiscoverPackagesAsync(projectFolderPath);

        // Record the outcome in the project load report before raising the
        // error banner, so the details the banner points at are already on
        // disk when the user goes looking.
        _loadReporter.RecordPackageReport(report);
        await _loadReporter.FlushAsync();

        if (report.Failures.Count > 0)
        {
            // Surface the failures via the console panel error banner.
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

    public IReadOnlyList<PackageLoadFailure> GetLoadFailures()
    {
        return _registry.GetLoadFailures();
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
