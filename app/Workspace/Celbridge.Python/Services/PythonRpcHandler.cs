using Microsoft.Extensions.Logging;

namespace Celbridge.Python.Services;

public class PythonRpcHandler
{
    private readonly ILogger<PythonRpcHandler> _logger;

    public PythonRpcHandler(ILogger<PythonRpcHandler> logger)
    {
        _logger = logger;
    }

    // Example handler method that Python can call
    // Note: These methods should NOT return Result<T> - they should throw exceptions
    // on error, which StreamJsonRpc will propagate back to Python as RPC errors.

    public Task LogMessageAsync(string level, string message)
    {
        switch (level.ToLowerInvariant())
        {
            case "debug":
                _logger.LogDebug("[Python] {Message}", message);
                break;
            case "info":
                _logger.LogInformation("[Python] {Message}", message);
                break;
            case "warning":
                _logger.LogWarning("[Python] {Message}", message);
                break;
            case "error":
                _logger.LogError("[Python] {Message}", message);
                break;
            default:
                _logger.LogInformation("[Python] {Message}", message);
                break;
        }

        return Task.CompletedTask;
    }
}
