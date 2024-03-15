using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.StatusBar.ViewModels;

public class StatusPanelViewModel
{
    IStatusService _statusService;

    public StatusPanelViewModel(IUserInterfaceService userInterfaceService,
        IStatusService statusService)
    {
        _statusService = statusService; // Transient instance created via DI

        // Register the status service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_statusService);
    }
}
