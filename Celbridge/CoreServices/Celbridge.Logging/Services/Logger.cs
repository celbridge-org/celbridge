using Microsoft.Extensions.Logging;

namespace Celbridge.Logging.Services;

public class Logger<T> : ILogger<T>
{
    private Microsoft.Extensions.Logging.ILogger<T> _logger;

    public Logger(Microsoft.Extensions.Logging.ILogger<T> logger)
    {
        _logger = logger;
    }

    public void LogDebug(Exception? exception, string? message, params object?[] args)
    {
        _logger.LogDebug(exception, message, args);
    }

    public void LogDebug(string? message, params object?[] args)
    {
        _logger.LogDebug(message, args);
    }

    public void LogTrace(Exception? exception, string? message, params object?[] args)
    {
        _logger.LogTrace(exception, message, args);
    }

    public void LogTrace(string? message, params object?[] args)
    {
        _logger.LogTrace(message, args);
    }

    public void LogInformation(Exception? exception, string? message, params object?[] args)
    {
        _logger.LogInformation(exception, message, args);
    }

    public void LogInformation(string? message, params object?[] args)
    {
        _logger.LogInformation(message, args);
    }

    public void LogWarning(Exception? exception, string? message, params object?[] args)
    {
        _logger.LogWarning(exception, message, args);
    }

    public void LogWarning(string? message, params object?[] args)
    {
        _logger.LogWarning(message, args);
    }

    public void LogWarning(Result result, string? message, params object?[] args)
    {
        string output = GetErrorResultMessage(result, message, args);
        if (!string.IsNullOrEmpty(output))
        {
            _logger.LogWarning(output);
        }
    }

    public void LogError(Exception? exception, string? message, params object?[] args)
    {
        _logger.LogError(exception, message, args);
    }

    public void LogError(string? message, params object?[] args)
    {
        _logger.LogError(message, args);
    }

    public void LogError(Result result, string? message, params object?[] args)
    {
        string output = GetErrorResultMessage(result, message, args);
        if (!string.IsNullOrEmpty(output))
        {
            _logger.LogError(output);
        }
    }

    public void LogCritical(Exception? exception, string? message, params object?[] args)
    {
        _logger.LogCritical(exception, message, args);
    }

    public void LogCritical(string? message, params object?[] args)
    {
        _logger.LogCritical(message, args);
    }

    public void LogCritical(Result result, string? message, params object?[] args)
    {
        string output = GetErrorResultMessage(result, message, args);
        if (!string.IsNullOrEmpty(output))
        {
            _logger.LogCritical(output);
        }
    }

    public IDisposable? BeginScope(string messageFormat, params object?[] args)
    {
        string message = string.Format(messageFormat, args);
        return NLog.ScopeContext.PushProperty("Scope", message);
    }

    public void Shutdown()
    {
        NLog.LogManager.Shutdown();
    }

    private static string GetErrorResultMessage(Result result, string? message, object?[] args)
    {
        string output = string.Empty;
        if (!string.IsNullOrEmpty(message))
        {
            output = string.Format(message, args);
        }

        if (result.IsFailure)
        {
            if (!string.IsNullOrEmpty(output))
            {
                output += Environment.NewLine;
            }
            output += result.Error;
        }

        return output;
    }
}
