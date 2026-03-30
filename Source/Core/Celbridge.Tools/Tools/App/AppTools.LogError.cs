using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Logs an error message to the application log.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    [McpServerTool(Name = "app_log_error", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log_error")]
    public partial void LogError(string message)
    {
        Logger.LogError(message);
    }
}
