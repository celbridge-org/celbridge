namespace Celbridge.Utilities.Services;

public class Logger : ILogFile
{
    private readonly IUtilityService _utilityService;

    private string _logFilePath = string.Empty;

    public Logger(IUtilityService utilityService)
    {
        _utilityService = utilityService;
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
            if (maxFilesToKeep <= 0)
            {
                var logFilename = $"{logFilePrefix}.jsonl";
                _logFilePath = Path.Combine(logFolderPath, logFilename);
            }
            else
            {
                // If we have multiple log files in rotation, append a timestamp to the filename to differentiate them.
                var timestamp = _utilityService.GetTimestamp();
                var logFilename = $"{logFilePrefix}_{timestamp}.jsonl";
                _logFilePath = Path.Combine(logFolderPath, logFilename);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to initialize logger. {ex}");
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
                // Write the log message with a newline
                writer.WriteLine(line);
            }

        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write to log. {ex}");
        }

        return Result.Ok();
    }

    public Result ClearLogFile()
    {
        try
        {
            File.WriteAllText(_logFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to clear log file. {ex}");
        }

        return Result.Ok();
    }
}
