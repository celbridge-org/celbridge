using Celbridge.Workspace;

namespace Celbridge.Entities.Services;

public class ComponentProxyService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ComponentProxyService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Initialize()
    {
        return Result.Ok();
    }
}
