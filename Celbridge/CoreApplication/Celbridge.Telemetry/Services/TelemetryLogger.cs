using Celbridge.Utilities;
using Celbridge.Core;

namespace Celbridge.Telemetry.Services;

public class TelemetryLogger
{
    private const string LogFilePrefix = "TelemetryLog";

    private readonly ILogger _logger;

    public TelemetryLogger(
        ILogger logger)
    {
        _logger = logger;
    }

    public Result Initialize(string logFolderPath, int maxFilesToKeep)
    {
        var initResult = _logger.Initialize(logFolderPath, LogFilePrefix, maxFilesToKeep);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        return Result.Ok();
    }

    public Result WriteObject(object? obj)
    {
        return _logger.WriteObject(obj);
    }

    public Result WriteLine(string line)
    {
        return _logger.WriteLine(line);
    }
}
