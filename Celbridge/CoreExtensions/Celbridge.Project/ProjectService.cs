using Celbridge.BaseLibrary.Project;

namespace Celbridge.Project;

public class ProjectService : IProjectService
{
    private readonly IProjectAdminService _projectAdminService;

    // Convenience accessor for getting the loaded project data
    public IProjectData LoadedProjectData => _projectAdminService.LoadedProjectData!;

    public ProjectService(IProjectAdminService projectAdminService)
    {
        _projectAdminService = projectAdminService;
    }
}
