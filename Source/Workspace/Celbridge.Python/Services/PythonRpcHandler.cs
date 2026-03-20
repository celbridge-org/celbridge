using Celbridge.ApplicationEnvironment;
using Microsoft.Extensions.Logging;

namespace Celbridge.Python.Services;

/// <summary>
/// Handles JSON-RPC method calls from the Python connector.
/// Methods on this class are automatically exposed to Python via StreamJsonRpc.
/// </summary>
public class PythonRpcHandler
{
    private readonly ILogger<PythonRpcHandler> _logger;
    private readonly IEnvironmentService _environmentService;

    public PythonRpcHandler(
        ILogger<PythonRpcHandler> logger,
        IEnvironmentService environmentService)
    {
        _logger = logger;
        _environmentService = environmentService;
    }

    /// <summary>
    /// Writes a log message from the Python connector to the application log.
    /// Called from Python via: cel.log(message="...")
    /// </summary>
    public void Log(string message)
    {
        _logger.LogInformation("[Python] {Message}", message);
    }

    /// <summary>
    /// Returns the Celbridge application version string.
    /// Called from Python via: cel.get_app_version()
    /// </summary>
    public string GetAppVersion()
    {
        var environmentInfo = _environmentService.GetEnvironmentInfo();
        return environmentInfo.AppVersion;
    }
}
