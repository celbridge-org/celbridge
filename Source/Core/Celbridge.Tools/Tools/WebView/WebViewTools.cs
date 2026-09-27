using System.Text.Json;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for inspecting and exercising custom editor WebViews, and the
/// pages they preview. Provides agents authoring custom editors with a feedback loop:
/// reload after a package edit, evaluate JavaScript, and inspect DOM, console, and network state.
/// </summary>
[McpServerToolType]
public partial class WebViewTools : AgentToolBase
{
    private ILogger<WebViewTools>? _logger;

    public WebViewTools(IApplicationServiceProvider services) : base(services) { }

    private ILogger<WebViewTools> Logger => _logger ??= GetRequiredService<ILogger<WebViewTools>>();

    // Names the frame a call acted on, for a result whose own text has no room for it.
    private static string SerializeFrameMetadata(string frame)
    {
        return JsonSerializer.Serialize(new FrameMetadata(frame), JsonOptions);
    }

    private sealed record FrameMetadata(string Frame);
}
