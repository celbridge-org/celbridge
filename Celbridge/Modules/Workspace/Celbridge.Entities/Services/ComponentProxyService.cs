using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Entities.Services;

public class ComponentProxyService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private IEntityService? _entityService;

    public ComponentProxyService(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Initialize()
    {
        _entityService = _workspaceWrapper.WorkspaceService.EntityService;

        return Result.Ok();
    }

    //public Result<IComponentProxy> AcquireComponentProxy(ResourceKey resource, int componentIndex)
    //{
    //    Guard.IsNotNull(_entityService);

    //    _entityService.GetComponentConfig()

    //    public ComponentProxy(IServiceProvider serviceProvider, ResourceKey resource, int componentIndex, ComponentSchema schema, IComponentDescriptor descriptor);

    //}
}
