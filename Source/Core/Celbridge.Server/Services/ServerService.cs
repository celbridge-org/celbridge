using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Server.Services;

/// <summary>
/// Coordinates the server infrastructure. Builds a fresh Kestrel instance for
/// each loaded workspace, delegates endpoint configuration to AgentServer and
/// FileServer, and disposes the instance when the workspace unloads. The
/// dynamically assigned port is captured on first start and reused on
/// subsequent starts so URLs remain stable across the application session.
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
    private int _persistentPort;
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
    }

    public async Task StartAsync()
    {
        if (!_featureFlags.IsEnabled(FeatureFlagConstants.McpTools))
        {
            Status = ServerStatus.Ready;
            _logger.LogInformation("MCP tools disabled by feature flag. Server will not start.");
            return;
        }

        if (_webApplication is not null)
        {
            _logger.LogWarning("Server is already running. StartAsync call ignored.");
            return;
        }

        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            Status = ServerStatus.Error;
            _logger.LogError("Cannot start server because no project is loaded.");
            return;
        }

        Status = ServerStatus.Starting;

        try
        {
            var builder = WebApplication.CreateSlimBuilder();

            // Bind to the previously assigned port if we have one. Port 0 lets
            // Kestrel pick any free port on the first start of the session.
            var bindUrl = _persistentPort == 0
                ? "http://127.0.0.1:0"
                : $"http://127.0.0.1:{_persistentPort}";
            builder.WebHost.UseUrls(bindUrl);

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

            // Read the actual port Kestrel assigned and remember it for the
            // remainder of the application session.
            var addresses = _webApplication.Urls;
            foreach (var address in addresses)
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                {
                    Port = uri.Port;
                    _persistentPort = uri.Port;
                    break;
                }
            }

            _fileServer.Enable(currentProject.ProjectFolderPath, Port);

            Status = ServerStatus.Ready;
            _logger.LogInformation("Server started on port {Port}", Port);

            _messengerService.Send(new ProjectFileServerReadyMessage());
        }
        catch (Exception exception)
        {
            Status = ServerStatus.Error;
            _logger.LogError(exception, "Failed to start server");

            // Ensure a partially constructed instance is torn down so the next
            // start attempt begins from a clean state.
            await TryDisposeWebApplicationAsync();
        }
    }

    public async Task StopAsync()
    {
        if (_webApplication is null)
        {
            Status = ServerStatus.NotStarted;
            Port = 0;
            return;
        }

        _fileServer.Disable();

        await TryDisposeWebApplicationAsync();

        Port = 0;
        Status = ServerStatus.NotStarted;
        _logger.LogInformation("Server stopped. Port {Port} retained for the next start.", _persistentPort);

        _messengerService.Send(new ServerStoppedMessage());
    }

    private async Task TryDisposeWebApplicationAsync()
    {
        if (_webApplication is null)
        {
            return;
        }

        try
        {
            await _webApplication.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose web application cleanly");
        }
        finally
        {
            _webApplication = null;
        }
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
