using Celbridge.FileSystem;

using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Celbridge.Logging;

/// <summary>
/// Deletes the oldest application log files so the log folder does not grow without bound.
/// Every application run writes to its own timestamped log file, so the folder gains a file per launch.
/// </summary>
[AllowDirectFileSystemAccess]
public static class LogFileRetention
{
    // Matches the log file names built at application startup.
    private const string LogFileSearchPattern = "celbridge_*.log";

    /// <summary>
    /// Deletes all but the newest retainedFileCount log files in the given folder. The starting run's own
    /// log file is always kept and does not count towards the limit. Called before dependency injection is
    /// available, so failures are swallowed rather than logged.
    /// </summary>
    public static void DeleteOldLogFiles(string logFolderPath, string currentLogFilePath, int retainedFileCount)
    {
        if (retainedFileCount < 0)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(logFolderPath))
            {
                return;
            }

            var logFilePaths = new List<string>(Directory.GetFiles(logFolderPath, LogFileSearchPattern));

            // This run appends to its own file for the lifetime of the process, so that file is never a
            // deletion candidate and does not count towards the retained total.
            var currentLogFileName = Path.GetFileName(currentLogFilePath);
            logFilePaths.RemoveAll(logFilePath => HasFileName(logFilePath, currentLogFileName));

            if (logFilePaths.Count <= retainedFileCount)
            {
                return;
            }

            // Each log file name embeds a fixed width, zero padded launch timestamp, so ordering the names
            // ascending puts the oldest first without reading write times from disk.
            logFilePaths.Sort(StringComparer.OrdinalIgnoreCase);

            var deletionCount = logFilePaths.Count - retainedFileCount;
            for (var index = 0; index < deletionCount; index++)
            {
                DeleteLogFile(logFilePaths[index]);
            }
        }
        catch
        {
            // Log retention is housekeeping. It must never prevent the application from starting.
        }
    }

    private static bool HasFileName(string logFilePath, string fileName)
    {
        var candidateFileName = Path.GetFileName(logFilePath);

        return string.Equals(candidateFileName, fileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteLogFile(string logFilePath)
    {
        try
        {
            File.Delete(logFilePath);
        }
        catch
        {
            // A concurrently running Celbridge instance may still hold this file open. Leave it in place
            // and let a later run collect it.
        }
    }
}
