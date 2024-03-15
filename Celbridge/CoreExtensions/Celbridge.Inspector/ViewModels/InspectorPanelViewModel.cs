using Celbridge.BaseLibrary.Inspector;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Inspector.ViewModels;

public class InspectorPanelViewModel
{
    IInspectorService _inspectorService;

    public InspectorPanelViewModel(
        IUserInterfaceService userInterfaceService,
        IInspectorService inspectorService)
    {
        _inspectorService = inspectorService; // Transient instance created via DI

        // Register the project service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_inspectorService);
    }
}
