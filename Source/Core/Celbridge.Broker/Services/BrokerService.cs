using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Broker.Services;

/// <summary>
/// Orchestrates broker infrastructure in response to workspace events.
/// Coordinates the MCP HTTP server, project file server, and MCP config
/// file management.
/// </summary>
public class BrokerService : IBrokerService
{
    private readonly IMcpHttpTransport _mcpHttpTransport;
    private readonly ProjectFileServer _projectFileServer;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly ILogger<BrokerService> _logger;

    public BrokerService(
        IMcpHttpTransport mcpHttpTransport,
        ProjectFileServer projectFileServer,
        IMessengerService messengerService,
        IProjectService projectService,
        ILogger<BrokerService> logger)
    {
        _mcpHttpTransport = mcpHttpTransport;
        _projectFileServer = projectFileServer;
        _messengerService = messengerService;
        _projectService = projectService;
        _logger = logger;

        messengerService.Register<McpServerReadyMessage>(this, OnMcpServerReady);
        messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    private void OnMcpServerReady(object recipient, McpServerReadyMessage message)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is not null)
        {
            EnableProjectServices(currentProject.ProjectFolderPath);
        }
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        var port = _mcpHttpTransport.Port;
        if (port == 0)
        {
            // MCP server hasn't started yet. McpServerReadyMessage will handle it.
            return;
        }

        EnableProjectServices(currentProject.ProjectFolderPath);
    }

    private void EnableProjectServices(string projectFolderPath)
    {
        var port = _mcpHttpTransport.Port;
        _projectFileServer.Enable(projectFolderPath, port);
        McpJsonConfigWriter.WriteConfigFile(projectFolderPath, port, _logger);

        // Notify documents that depend on local file serving to re-navigate
        _messengerService.Send(new ProjectFileServerReadyMessage());
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        _projectFileServer.Disable();
        McpJsonConfigWriter.RemoveConfigEntry(currentProject.ProjectFolderPath, _logger);
    }
}
