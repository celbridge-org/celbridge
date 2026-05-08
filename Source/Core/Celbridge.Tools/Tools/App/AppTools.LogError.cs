using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Log an ERROR message to the application log; for unrecoverable failures.</summary>
    [McpServerTool(Name = "app_log_error", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_error")]
    public partial void LogError(string message)
    {
        Logger.LogError(message);
    }
}
