using Path = System.IO.Path;

namespace Celbridge.Inspector.Services;

public class InspectorFactory : IInspectorFactory
{
    public InspectorFactory()
    {}

    public Result<IInspector> CreateGenericInspector(ResourceKey resource)
    {
        try
        {
            var inspector = new Views.Inspector()
            {
                DataContext = new ViewModels.InspectorViewModel()
                {
                    Resource = resource
                }
            };

            return Result<IInspector>.Ok(inspector);
        }
        catch (Exception ex) 
        {
            return Result<IInspector>.Fail($"An exception occurred when creating a generic inspector for resource: {resource}")
                .WithException(ex);        
        }
    }

    public Result<IInspector> CreateSpecializedInspector(ResourceKey resource)
    {
        try
        {
            var fileExtension = Path.GetExtension(resource);

            IInspector? inspector = null;
            //if (fileExtension == ".web")
            //{
            //    inspector = new Views.WebInspector()
            //    {
            //        DataContext = new ViewModels.WebInspectorViewModel()
            //        {
            //            Resource = resource
            //        }
            //    };
            //}

            if (inspector is null)
            {
                return Result<IInspector>.Fail($"There is no specialized inspector available for resource: {resource}");
            }

            return Result<IInspector>.Ok(inspector);
        }
        catch (Exception ex)
        {
            return Result<IInspector>.Fail($"An exception occurred when creating a generic inspector for resource: {resource}")
                .WithException(ex);
        }
    }
}

