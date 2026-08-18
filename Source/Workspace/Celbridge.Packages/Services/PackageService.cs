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
        // Persisting discovery: DiscoverPackagesAsync writes the normalized config back only when
        // discovery is clean (so a package that failed to load never nukes its own config on disk) and
        // the config parsed cleanly. Both gates are applied inside the reconcile.
        var report = await _registry.DiscoverPackagesAsync(projectFolderPath, persistNormalizedConfig: true);

        // Flushed here as well as at the end of the load, so a load that never reaches the end still
        // leaves the package outcome on disk. Every flush during a load writes the same file.
        _loadReporter.RecordPackageReport(report);
        await _loadReporter.FlushAsync();

        // Failures reach the user through the load report recorded above, not from here.
        _messengerService.Send(new PackagesInitializedMessage());
    }

    public async Task RescanProjectPackagesAsync(string projectFolderPath)
    {
        // A rescan refreshes the in-memory registry but never rewrites the project file.
        await _registry.DiscoverPackagesAsync(projectFolderPath, persistNormalizedConfig: false);
    }

    public IReadOnlyList<Package> GetAllPackages()
    {
        return _registry.GetAllPackages();
    }

    public IReadOnlyList<ContributionIssue> GetContributionIssues()
    {
        return _registry.GetContributionIssues();
    }

    public IReadOnlyList<PackageLoadFailure> GetLoadFailures()
    {
        return _registry.GetLoadFailures();
    }

    public IReadOnlyList<EditorContribution> GetAllEditors()
    {
        return _registry.GetAllEditors();
    }

    public IReadOnlyList<ResolvedEditor> GetResolvedEditors()
    {
        return _registry.GetResolvedEditors();
    }

    public ProjectConfig? GetNormalizedConfig()
    {
        return _registry.GetNormalizedConfig();
    }

    public IReadOnlyList<ResolvedEditor> GetBuiltInEditors()
    {
        return _registry.GetBuiltInEditors();
    }

    public Package? GetContributingPackage(EditorId editorId)
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

    public byte[]? GetUtilityTemplateContent(EditorContribution contribution)
    {
        return _registry.GetUtilityTemplateContent(contribution);
    }
}
