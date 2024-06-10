using Celbridge.BaseLibrary.Project;
using Celbridge.Project.Views;

namespace Celbridge.Project;

public class ProjectService : IProjectService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectDataService _projectDataService;

    // Convenience accessor for getting the loaded project data
    public IProjectData LoadedProjectData => _projectDataService.LoadedProjectData!;

    public ProjectService(
        IServiceProvider serviceProvider,
        IProjectDataService projectDataService)
    {
        _serviceProvider = serviceProvider;
        _projectDataService = projectDataService;
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
