using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Inspector;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.UserInterface;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    private IProjectService? _projectService;
    public IProjectService ProjectService 
    {
        get => _projectService!;
        private set => _projectService = value; 
    }

    private IStatusService? _statusService;
    public IStatusService StatusService 
    { 
        get => _statusService!;
        private set => _statusService = value; 
    }

    private IConsoleService? _consoleService;
    public IConsoleService ConsoleService 
    { 
        get => _consoleService!;
        private set => _consoleService = value; 
    }

    private IInspectorService? _inspectorService;
    public IInspectorService InspectorService
    {
        get => _inspectorService!;
        private set => _inspectorService = value;
    }

    public WorkspaceService(IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        // Clients should not reference a WorkspaceService after the workspace is unloaded.
        // This ensures that even if they do, all the subservices will no longer be available.

        _projectService = null;
        _statusService = null;
        _consoleService = null;
        _inspectorService = null;

        _messengerService.Unregister<WorkspaceLoadedMessage>(this);
    }

    /// <summary>
    /// Instantiate the workspace panels that are registered with the UserInterfaceService.
    /// </summary>
    public Dictionary<WorkspacePanelType, UIElement> CreateWorkspacePanels()
    {
        var panels = new Dictionary<WorkspacePanelType, UIElement>();
        foreach (var config in _userInterfaceService.WorkspacePanelConfigs)
        {
            if (panels.ContainsKey(config.PanelType))
            {
                throw new InvalidOperationException($"Panel type '{config.PanelType}' is already registered.");
            }   

            // Instantiate the panel
            var panel = _serviceProvider.GetRequiredService(config.ViewType) as UIElement;
            if (panel is null)
            {
                throw new Exception($"Failed to create a workspace panel of type '{config.ViewType}'");
            }

            panels.Add(config.PanelType, panel);
        }

        return panels;
    }

    public void RegisterService(object workspaceService)
    {
        switch (workspaceService)
        {
            case IProjectService projectService:
                ProjectService = projectService;
                break;

            case IStatusService statusService:
                StatusService = statusService;
                break;

            case IConsoleService consoleService:
                ConsoleService = consoleService;
                break;

            case IInspectorService inspectorService:
                InspectorService = inspectorService;
                break;

            default:
                throw new InvalidOperationException($"Failed to register workspace service because the service type '{workspaceService.GetType()}' is not recognized");
        }
    }
}
