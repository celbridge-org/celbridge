using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.Project.Resources;
using Celbridge.Project.Views;

namespace Celbridge.Project;

public class ProjectService : IProjectService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectDataService _projectDataService;

    public IResourceRegistry ResourceRegistry { get; init; }

    public ProjectService(
        IServiceProvider serviceProvider,
        IProjectDataService projectDataService)
    {
        _serviceProvider = serviceProvider;
        _projectDataService = projectDataService;

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
