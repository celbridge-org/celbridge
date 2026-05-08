using Celbridge.ApplicationEnvironment;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Returns the running Celbridge version as a major.minor.patch string.</summary>
    [McpServerTool(Name = "app_get_version", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_version")]
    public partial CallToolResult AppVersion()
    {
        var environmentService = GetRequiredService<IEnvironmentService>();
        var environmentInfo = environmentService.GetEnvironmentInfo();
        return ToolSuccess(environmentInfo.AppVersion);
    }
}
