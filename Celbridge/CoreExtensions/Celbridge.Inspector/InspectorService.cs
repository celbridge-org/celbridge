using Celbridge.BaseLibrary.Inspector;
using Celbridge.Inspector.Views;

namespace Celbridge.Inspector;

public class InspectorService : IInspectorService
{
    private readonly IServiceProvider _serviceProvider;

    public InspectorService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateInspectorPanel()
    {
        return _serviceProvider.GetRequiredService<InspectorPanel>();
    }
}
