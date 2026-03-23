using Celbridge.MCPTools.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Broker.Services;

/// <summary>
/// Hosts an MCP HTTP server on localhost using embedded Kestrel.
/// AI agents connect to this server to discover and invoke tools
/// via the standard MCP protocol. The port is dynamically assigned.
/// </summary>
public class McpHttpTransport : IMcpHttpTransport
{
    private readonly IServiceProvider _applicationServices;
    private readonly ProjectFileServer _projectFileServer;
    private readonly ILogger<McpHttpTransport> _logger;

    private WebApplication? _webApplication;
    private bool _disposed;

    public int Port { get; private set; }

    public McpHttpTransport(
        IServiceProvider applicationServices,
        ProjectFileServer projectFileServer,
        ILogger<McpHttpTransport> logger)
    {
        _applicationServices = applicationServices;
        _projectFileServer = projectFileServer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Use port 0 so Kestrel picks an available port
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Make the main application's services available to MCP tool classes.
        // Tools take IApplicationServiceProvider and resolve what they need.
        var applicationServiceProvider = new ApplicationServiceProvider(_applicationServices);
        builder.Services.AddSingleton<IApplicationServiceProvider>(applicationServiceProvider);

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(AppTools).Assembly);

        _webApplication = builder.Build();
        _webApplication.MapMcp("/mcp");
        _projectFileServer.ConfigureEndpoint(_webApplication);

        await _webApplication.StartAsync(cancellationToken);

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

        _logger.LogInformation("MCP HTTP server listening on port {Port}", Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_webApplication is not null)
        {
            _logger.LogInformation("Stopping MCP HTTP server on port {Port}", Port);
            await _webApplication.StopAsync(cancellationToken);
            await _webApplication.DisposeAsync();
            _webApplication = null;
            Port = 0;
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

    ~McpHttpTransport()
    {
        Dispose(false);
    }
}
