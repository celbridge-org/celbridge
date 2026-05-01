using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for inspecting and exercising contribution editor and HTML viewer
/// WebViews. Provides agents authoring custom editors with a feedback loop:
/// reload after a package edit, evaluate JavaScript, and inspect DOM, console, and network state.
/// </summary>
[McpServerToolType]
public partial class WebViewTools : AgentToolBase
{
    private ILogger<WebViewTools>? _logger;

    public WebViewTools(IApplicationServiceProvider services) : base(services) { }

    private ILogger<WebViewTools> Logger => _logger ??= GetRequiredService<ILogger<WebViewTools>>();
}
