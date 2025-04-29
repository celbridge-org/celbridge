using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;
using Celbridge.Workspace;

using Path = System.IO.Path;

namespace Celbridge.Inspector.Services;

public class InspectorFactory : IInspectorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public InspectorFactory(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result<IInspector> CreateResourceNameInspector(ResourceKey resource)
    {
        try
        {
            var inspector = CreateInspector<ResourceNameInspector, ResourceNameInspectorViewModel>(resource);
            return Result<IInspector>.Ok(inspector);
        }
        catch (Exception ex) 
        {
            return Result<IInspector>.Fail($"An exception occurred when creating the name inspector for resource: {resource}")
                .WithException(ex);        
        }
    }

    public Result<IInspector> CreateComponentListView(ResourceKey resource)
    {
        try
        {
            var inspector = CreateInspector<ComponentListView, ComponentListViewModel>(resource);
            return Result<IInspector>.Ok(inspector);
        }
        catch (Exception ex)
        {
            return Result<IInspector>.Fail($"An exception occurred when creating the entity inspector for resource: {resource}")
                .WithException(ex);
        }
    }

    public Result<IInspector> CreateResourceInspector(ResourceKey resource)
    {
        try
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
            var path = resourceRegistry.GetResourcePath(resource);

            if (Directory.Exists(path))
            {
                return CreateFolderInspector(resource);
            }

            if (File.Exists(path))
            {
                return CreateFileInspector(resource);
            }

            return Result<IInspector>.Fail($"Resource not found at path: {path}");
        }
        catch (Exception ex)
        {
            return Result<IInspector>.Fail($"An exception occurred when creating a generic inspector for resource: {resource}")
                .WithException(ex);
        }
    }

    private Result<IInspector> CreateFolderInspector(ResourceKey resource)
    {
        return Result<IInspector>.Fail($"There is no inspector implemented for this resource type: {resource}");
    }

    private Result<IInspector> CreateFileInspector(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource);

        IInspector? inspector = null;
        if (extension == ".web")
        {
            inspector = CreateInspector<WebInspector, WebInspectorViewModel>(resource);
        }

        if (inspector is not null)
        {
            return Result<IInspector>.Ok(inspector);
        }

        return Result<IInspector>.Fail($"There is no inspector available for this resource: {resource}");
    }

    private IInspector CreateInspector<TView, TViewModel>(ResourceKey resource)
        where TView : IInspector
        where TViewModel : InspectorViewModel
    {
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        viewModel.Resource = resource;
        var inspector = (IInspector)Activator.CreateInstance(typeof(TView), viewModel)!;
        return inspector;
    }
}
