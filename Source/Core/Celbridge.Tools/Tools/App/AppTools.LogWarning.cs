using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Logs a warning message to the application log.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    [McpServerTool(Name = "app_log_warning", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_warning")]
    public partial void LogWarning(string message)
    {
        Logger.LogWarning(message);
    }
}
