using Celbridge.BaseLibrary.Project;

namespace Celbridge.Project;

public class ProjectService : IProjectService
{
    private readonly IProjectAdminService _projectAdminService;

    // Convenience accessor for getting the loaded project data
    public IProjectData ProjectData => _projectAdminService.ProjectData!;

    public ProjectService(IProjectAdminService projectAdminService)
    {
        _projectAdminService = projectAdminService;
    }
}
