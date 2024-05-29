using Celbridge.BaseLibrary.Project;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Project;

public class ProjectService : IProjectService
{
    private readonly IMessengerService _messengerService;

    private IProjectData? _projectData;
    public IProjectData ProjectData
    {
        get
        {
            Guard.IsNotNull(_projectData);
            return _projectData;
        }
    }

    public ProjectService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void Initialize(IProjectData projectData)
    { 
        Guard.IsNotNull(projectData);
        Guard.IsNull(_projectData); // May only initialize once

        _projectData = projectData;

        var message = new ProjectDataInitializedMessage(projectData);
        _messengerService.Send(message);
    }
}
