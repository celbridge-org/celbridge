namespace Celbridge.Inspector.Services;

public class InspectorFactory : IInspectorFactory
{
    public InspectorFactory()
    {}

    public IInspector CreateInspector(ResourceKey resource)
    {
        // Todo: Create inspectors for all supported file types

        var inspector = new Views.Inspector()
        {
            DataContext = new ViewModels.InspectorViewModel()
            {
                Resource = resource
            }
        };

        return inspector;
    }
}

