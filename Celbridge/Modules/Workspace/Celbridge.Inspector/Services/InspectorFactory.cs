using Celbridge.Inspector.ViewModels;

namespace Celbridge.Inspector.Services;

public class InspectorFactory : IInspectorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public InspectorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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
}

