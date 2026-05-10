using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Log a WARNING message to the application log; for unexpected-but-recoverable situations.</summary>
    [McpServerTool(Name = "app_log_warning", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_warning")]
    [RelatedGuides]
    public partial void LogWarning(string message)
    {
        Logger.LogWarning(message);
    }
}
