using Celbridge.ApplicationEnvironment;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Returns the application version string.
    /// </summary>
    /// <returns>A version string in the format "major.minor.patch", e.g. "0.2.5".</returns>
    [McpServerTool(Name = "app_get_version", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.get_version")]
    public partial CallToolResult AppVersion()
    {
        var environmentService = GetRequiredService<IEnvironmentService>();
        var environmentInfo = environmentService.GetEnvironmentInfo();
        return SuccessResult(environmentInfo.AppVersion);
    }
}
