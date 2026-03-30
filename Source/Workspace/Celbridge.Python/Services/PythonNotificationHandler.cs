using Celbridge.Logging;

namespace Celbridge.Python.Services;

/// <summary>
/// Handles JSON-RPC notifications sent by the Python process at startup.
/// Registered as an additional RPC target on the TCP transport so the
/// Python process can report its environment info (e.g., installed packages).
/// StreamJsonRpc automatically maps the public method name to a camelCase
/// RPC method name, so PythonReady becomes "pythonReady" on the wire.
/// </summary>
public class PythonNotificationHandler
{
    private readonly ILogger _logger;

    public PythonNotificationHandler(ILogger logger)
    {
        _logger = logger;
    }

    public void PythonReady(List<string> packages)
    {
        PythonEnvironmentInfo.InstalledPackages = packages;
        _logger.LogInformation("Python environment reported {PackageCount} installed packages", packages.Count);
    }
}
