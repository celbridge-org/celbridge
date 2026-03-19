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

    public Task LogMessageAsync(string message)
    {
        _logger.LogInformation("[Python] {Message}", message);

        return Task.CompletedTask;
    }
}
