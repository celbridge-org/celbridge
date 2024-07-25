using Celbridge.Core;

namespace Celbridge.Utilities.Services;

public class Logger : ILogger
{
    private readonly IUtilityService _utilityService;
    private readonly ILogSerializer _serializer;

    private string _logFilePath = string.Empty;

    public Logger(
        IUtilityService utilityService,
        ILogSerializer commandLogSerializer)
    {
        _utilityService = utilityService;
        _serializer = commandLogSerializer;
    }

    public Result Initialize(string logFolderPath, string logFilePrefix, int maxFilesToKeep)
    {
        try
        {
            // Aqcuire the log folder
            if (Directory.Exists(logFolderPath))
            {
                // Delete old log files that start with the same prefix
                var deleteResult = _utilityService.DeleteOldFiles(logFolderPath, logFilePrefix, maxFilesToKeep);
                if (deleteResult.IsFailure)
                {
                    return deleteResult;
                }
            }
            else
            {
                Directory.CreateDirectory(logFolderPath);
            }

            // Generate the log file path
            var timestamp = _utilityService.GetTimestamp();
            var logFilename = $"{logFilePrefix}_{timestamp}.jsonl";
            _logFilePath = Path.Combine(logFolderPath, logFilename);

            // Write environment info as the first record in the log
            var environmentInfo = _utilityService.GetEnvironmentInfo();
            var writeResult = WriteObject(environmentInfo);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to initialize logger. {ex}");
        }

        return Result.Ok();
    }

    public Result WriteObject(object? obj)
    {
        if (obj is null)
        {
            return Result.Fail($"Object is null");
        }

        try
        {
            string logEntry = _serializer.SerializeObject(obj, false);
            var writeResult = WriteLine(logEntry);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write object to log. {ex}");
        }

        return Result.Ok();
    }

    public Result WriteLine(string line)
    {
        try
        {
            using (var fileStream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write))
            using (var writer = new StreamWriter(fileStream))
            {
                // Write the log message with a newline character
                writer.WriteLine(line);
            }

        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write to log. {ex}");
        }

        return Result.Ok();
    }
}
