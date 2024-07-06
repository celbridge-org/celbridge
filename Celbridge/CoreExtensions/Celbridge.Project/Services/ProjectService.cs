using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.Project.Views;
using Celbridge.Utilities.Services;

namespace Celbridge.Project.Services;

public class ProjectService : IProjectService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectDataService _projectDataService;

    public IResourceRegistry ResourceRegistry { get; init; }

    public ProjectService(
        IServiceProvider serviceProvider,
        IProjectDataService projectDataService,
        IUtilityService utilityService)
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
        var projectFolder = _projectDataService.LoadedProjectData!.ProjectFolder;
        ResourceRegistry = new ResourceRegistry(projectFolder);
    }

    public object CreateProjectPanel()
    {
        return _serviceProvider.GetRequiredService<ProjectPanel>();
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
