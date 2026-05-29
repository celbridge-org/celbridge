using Celbridge.Explorer;
using Celbridge.Inspector.ViewModels;
using Celbridge.Inspector.Views;
using Celbridge.Workspace;

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

    public async Task<Result<IInspector>> CreateResourceInspectorAsync(ResourceKey resource)
    {
        try
        {
            var fileStorage = _workspaceWrapper.WorkspaceService.FileStorage;
            var infoResult = await fileStorage.GetInfoAsync(resource);
            if (infoResult.IsFailure)
            {
                return Result<IInspector>.Fail($"Failed to probe resource: '{resource}'")
                    .WithErrors(infoResult);
            }
            var info = infoResult.Value;

            if (info.Kind == StorageItemKind.Folder)
            {
                return CreateFolderInspector(resource);
            }

            if (info.Kind == StorageItemKind.File)
            {
                return CreateFileInspector(resource);
            }

            return Result<IInspector>.Fail($"Resource not found: '{resource}'");
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
        // WebViewExtension is multi-part (.webview.cel) so Path.GetExtension
        // would return only the final suffix. Match on the resource string instead.
        var resourceString = resource.ToString();

        IInspector? inspector = null;
        if (resourceString.EndsWith(ExplorerConstants.WebViewExtension, StringComparison.OrdinalIgnoreCase))
        {
            // WebInspector uses XAML with a parameterless constructor
            inspector = new WebInspector
            {
                Resource = resource
            };
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
