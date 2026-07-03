using Celbridge.Commands;
using Celbridge.Platform;

namespace Celbridge.UserInterface.Commands;

public class ShowLogsCommand : CommandBase, IShowLogsCommand
{
    private readonly IFileManagerLauncher _fileManagerLauncher;

    public ShowLogsCommand(IFileManagerLauncher fileManagerLauncher)
    {
        _fileManagerLauncher = fileManagerLauncher;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // The current run's log file path is published as an environment variable at startup, shared with
        // the NLog target and the Python host so every process logs to the same file.
        var logFilePath = Environment.GetEnvironmentVariable("CELBRIDGE_LOG_FILE");
        if (string.IsNullOrEmpty(logFilePath))
        {
            return Result.Fail("Failed to show application logs because the log file path is not set.");
        }

        // Reveals and selects the log file, falling back to its parent folder before the file is created.
        var openResult = await _fileManagerLauncher.OpenFileManagerAsync(logFilePath);
        if (openResult.IsFailure)
        {
            return Result.Fail("Failed to reveal the application log file in the file manager.")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void ShowLogs()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IShowLogsCommand>();
    }
}
