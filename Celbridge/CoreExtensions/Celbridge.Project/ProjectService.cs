using Celbridge.BaseLibrary.Project;
using Celbridge.Project.Views;

namespace Celbridge.Project;

public class ProjectService : IProjectService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectAdminService _projectAdminService;

    // Convenience accessor for getting the loaded project data
    public IProjectData LoadedProjectData => _projectAdminService.LoadedProjectData!;

    public ProjectService(
        IServiceProvider serviceProvider,
        IProjectAdminService projectAdminService)
    {
        _serviceProvider = serviceProvider;
        _projectAdminService = projectAdminService;
    }

    public object CreateProjectPanel()
    {
        return _serviceProvider.GetRequiredService<ProjectPanel>();
    }
}
