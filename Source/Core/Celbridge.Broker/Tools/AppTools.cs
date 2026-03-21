using Celbridge.ApplicationEnvironment;

namespace Celbridge.Broker.Tools;

/// <summary>
/// General application tools exposed to all broker clients.
/// </summary>
public static class AppTools
{
    [McpTool(Name = "app/version", Alias = "app_version", Description = "Returns the application version string")]
    public static string GetAppVersion()
    {
        var environmentService = ServiceLocator.AcquireService<IEnvironmentService>();
        var environmentInfo = environmentService.GetEnvironmentInfo();
        return environmentInfo.AppVersion;
    }
}
