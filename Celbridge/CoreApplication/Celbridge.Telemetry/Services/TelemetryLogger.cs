using Celbridge.Utilities;
using Celbridge.Core;

namespace Celbridge.Telemetry.Services;

public class TelemetryLogger
{
    private const string LogFilePrefix = "TelemetryLog";
    
    private readonly ILogSerializer _serializer;
    private readonly ILogger _logger;
    private readonly IUtilityService _utilityService;

    public TelemetryLogger(
        ILogSerializer logSerializer,
        ILogger logger,
        IUtilityService utilityService)
    {
        _serializer = logSerializer;
        _logger = logger;
        _utilityService = utilityService;
    }

    public Result Initialize(string logFolderPath, int maxFilesToKeep)
    {
        var initResult = _logger.Initialize(logFolderPath, LogFilePrefix, maxFilesToKeep);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        // Write environment info as the first record in the log
        var environmentInfo = _utilityService.GetEnvironmentInfo();
        var writeResult = WriteObject(environmentInfo);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        return Result.Ok();
    }

    public Result<string> WriteObject(object? obj)
    {
        if (obj is null)
        {
            return Result<string>.Fail($"Object is null");
        }

        try
        {
            // Strip out command properties for telemetry
            string json = _serializer.SerializeObject(obj, true);
            var writeResult = _logger.WriteLine(json);
            if (writeResult.IsFailure)
            {
                return Result<string>.Fail(writeResult.Error);
            }

            return Result<string>.Ok(json);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to write object to log. {ex}");
        }
    }
}
