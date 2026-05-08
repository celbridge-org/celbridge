using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Log an INFO message to the application log; for routine progress.</summary>
    [McpServerTool(Name = "app_log", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.log")]
    public partial void LogInfo(string message)
    {
        Logger.LogInformation(message);
    }
}
