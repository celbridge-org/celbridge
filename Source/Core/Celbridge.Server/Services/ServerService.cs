using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server.Services;

/// <summary>
/// Coordinates the server infrastructure. Creates and starts the shared
/// Kestrel instance, delegates endpoint configuration to AgentServer and
/// FileServer, and manages their lifecycle in response to workspace events.
/// </summary>
public class ServerService : IServerService, IDisposable
{
    private readonly IAgentServer _agentServer;
    private readonly IFileServer _fileServer;
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IServiceProvider _applicationServices;
    private readonly IFeatureFlags _featureFlags;
    private readonly ILogger<ServerService> _logger;

    private WebApplication? _webApplication;
    private bool _disposed;

    public ServerStatus Status { get; private set; }

    public int Port { get; private set; }

    public ServerService(
        IAgentServer agentServer,
        IFileServer fileServer,
        IMessengerService messengerService,
        IProjectService projectService,
        IServiceProvider applicationServices,
        IFeatureFlags featureFlags,
        ILogger<ServerService> logger)
    {
        _agentServer = agentServer;
        _fileServer = fileServer;
        _messengerService = messengerService;
        _projectService = projectService;
        _applicationServices = applicationServices;
        _featureFlags = featureFlags;
        _logger = logger;

        messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public async Task InitializeAsync()
    {
        if (!_featureFlags.IsEnabled(FeatureFlagConstants.McpTools))
        {
            Status = ServerStatus.Ready;
            _logger.LogInformation("MCP tools disabled by feature flag. Server will not start.");
            return;
        }

        Status = ServerStatus.Starting;

        try
        {
            var builder = WebApplication.CreateSlimBuilder();

            // Use port 0 so Kestrel picks an available port
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            // Make the main application's services available to MCP tool classes.
            // Tools take IApplicationServiceProvider and resolve what they need.
            var applicationServiceProvider = new ApplicationServiceProvider(_applicationServices);
            builder.Services.AddSingleton<IApplicationServiceProvider>(applicationServiceProvider);

            // Let AgentServer register MCP SDK services
            var agentServer = (AgentServer)_agentServer;
            agentServer.ConfigureServices(builder.Services);

            _webApplication = builder.Build();

            // Let AgentServer and FileServer configure their endpoints
            agentServer.ConfigureEndpoints(_webApplication);

            var fileServer = (FileServer)_fileServer;
            fileServer.ConfigureEndpoints(_webApplication);

            await _webApplication.StartAsync();

            // Read the actual port Kestrel assigned
            var addresses = _webApplication.Urls;
            foreach (var address in addresses)
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                {
                    Port = uri.Port;
                    break;
                }
            }

            Status = ServerStatus.Ready;
            _logger.LogInformation("Server initialized on port {Port}", Port);
        }
        catch (Exception exception)
        {
            Status = ServerStatus.Error;
            _logger.LogError(exception, "Failed to initialize server");
        }
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        _fileServer.Enable(currentProject.ProjectFolderPath, Port);

        _messengerService.Send(new ProjectFileServerReadyMessage());
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        _fileServer.Disable();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;

            if (disposing && _webApplication is not null)
            {
                _webApplication.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _webApplication = null;
            }
        }
    }
}
