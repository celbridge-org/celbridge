using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.Inspector;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService
{
    public bool IsLeftPanelVisible { get; }
    public bool IsRightPanelVisible { get; }
    public bool IsBottomPanelVisible { get; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    public IProjectService ProjectService { get; }
    public IStatusService StatusService { get; }
    public IConsoleService ConsoleService { get; }
    public IInspectorService InspectorService { get; }
    public IDocumentsService DocumentsService { get; }

    public WorkspaceService(IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;

        // Create instances of the required sub-services
        ProjectService = _serviceProvider.GetRequiredService<IProjectService>();
        StatusService = _serviceProvider.GetRequiredService<IStatusService>();
        ConsoleService = _serviceProvider.GetRequiredService<IConsoleService>();
        InspectorService = _serviceProvider.GetRequiredService<IInspectorService>();
        DocumentsService = _serviceProvider.GetRequiredService<IDocumentsService>();

        _messengerService.Register<WorkspaceServiceDestroyedMessage>(this, OnWorkspaceServiceDestroyed);
    }

    private void OnWorkspaceServiceDestroyed(object recipient, WorkspaceServiceDestroyedMessage message)
    {
        // Clients should not reference a WorkspaceService after the workspace is unloaded.
        // This ensures that even if they do, all the subservices will no longer be available.

        // Todo: Consider making the sub-sevices be IDisposable and dispose them here.
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
}
