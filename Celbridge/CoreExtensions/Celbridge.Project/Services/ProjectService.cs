using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Projects.Views;
using Celbridge.Utilities.Services;

namespace Celbridge.Projects.Services;

public class ProjectService : IProjectService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectDataService _projectDataService;

    public IResourceRegistry ResourceRegistry { get; init; }

    private IResourceTreeView? _resourceTreeView;
    public IResourceTreeView ResourceTreeView 
    {
        get
        {
            return _resourceTreeView ?? throw new NullReferenceException("ResourceTreeView is null.");
        }
        set 
        { 
            _resourceTreeView = value; 
        }
    }

    public ProjectService(
        IServiceProvider serviceProvider,
        IProjectDataService projectDataService,
        IUtilityService utilityService,
        IResourceRegistry resourceRegistry)
    {
        _serviceProvider = serviceProvider;
        _projectDataService = projectDataService;

        // Delete the DeletedFiles folder to clean these archives up.
        // The DeletedFiles folder contain archived files and folders from previous delete commands.
        var tempFilename = utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, string.Empty);
        var deletedFilesFolder = Path.GetDirectoryName(tempFilename)!;
        if (Directory.Exists(deletedFilesFolder))
        {
            Directory.Delete(deletedFilesFolder, true);
        }

        // Create the resource registry for the project.
        // The registry is populated later once the workspace UI is fully loaded.
        ResourceRegistry = resourceRegistry;
        ResourceRegistry.ProjectFolderPath = _projectDataService.LoadedProjectData!.ProjectFolderPath;
        
    }

    public object CreateProjectPanel()
    {
        return _serviceProvider.GetRequiredService<ProjectPanel>();
    }

    public async Task<Result> UpdateResourcesAsync()
    {
        var updateResult = ResourceRegistry.UpdateResourceRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {updateResult.Error}");
        }

        var populateResult = await ResourceTreeView.PopulateTreeView(ResourceRegistry);
        if (populateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {populateResult.Error}");
        }

        return Result.Ok();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
            }

            _disposed = true;
        }
    }

    ~ProjectService()
    {
        Dispose(false);
    }
}
