using Celbridge.Explorer;
using Celbridge.Inspector.ViewModels;
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
            var viewModel = _serviceProvider.GetRequiredService<ResourceNameInspectorViewModel>();
            viewModel.Resource = resource;
            var inspector = new Views.ResourceNameInspector(viewModel);

            return Result<IInspector>.Ok(inspector);
        }
        catch (Exception ex) 
        {
            return Result<IInspector>.Fail($"An exception occurred when creating a generic inspector for resource: {resource}")
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
            var viewModel = _serviceProvider.GetRequiredService<WebInspectorViewModel>();
            viewModel.Resource = resource;
            inspector = new Views.WebInspector(viewModel);
        }

        if (inspector is not null)
        {
            return Result<IInspector>.Ok(inspector);
        }

        return Result<IInspector>.Fail($"There is no inspector available for this resource: {resource}");
    }
}

