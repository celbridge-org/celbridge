using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Logs an informational message to the application log.
    /// </summary>
    /// <param name="message">The message to log.</param>
    [McpServerTool(Name = "app_log", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log")]
    public partial void LogInfo(string message)
    {
        Logger.LogInformation(message);
    }
}
