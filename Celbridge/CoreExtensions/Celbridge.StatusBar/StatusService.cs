using Celbridge.BaseLibrary.Status;
using Celbridge.StatusBar.Views;

namespace Celbridge.StatusBar;

public class StatusService : IStatusService
{
    private readonly IServiceProvider _serviceProvider;

    public StatusService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateStatusPanel()
    {
        return _serviceProvider.GetRequiredService<StatusPanel>();
    }
}
