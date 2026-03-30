using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// General application tools for version info, logging, and alerts.
/// </summary>
[McpServerToolType]
public partial class AppTools : AgentToolBase
{
    private ILogger<AppTools>? _logger;

    public AppTools(IApplicationServiceProvider services) : base(services) { }

    private ILogger<AppTools> Logger => _logger ??= GetRequiredService<ILogger<AppTools>>();
}
