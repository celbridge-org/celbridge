using Celbridge.FileSystem;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Writes a mode's start-up files into the project data folder before a console starts, rewriting any that
/// are missing or differ. An upgraded application or a deleted folder is then put right by the next console.
/// </summary>
public static class ConsoleStartupWriter
{
    private const string ConsoleFolderName = "console";
    private const string PassThroughFolderName = "pass_through";
    private const string CleanFolderName = "clean";
    private const string HistoryFolderName = "history";

    /// <summary>
    /// Writes the start-up files for the mode and makes the history folder, and returns where they are.
    /// </summary>
    public static async Task<Result<ConsoleStartupOptions>> WriteAsync(
        ILocalFileSystem fileSystem,
        string projectDataFolderPath,
        bool useShellProfile)
    {
        var consoleFolder = Path.Combine(projectDataFolderPath, ConsoleFolderName);
        var modeFolder = Path.Combine(consoleFolder, useShellProfile ? PassThroughFolderName : CleanFolderName);
        var historyFolder = Path.Combine(consoleFolder, HistoryFolderName);

        var historyResult = await fileSystem.CreateFolderAsync(historyFolder);
        if (historyResult.IsFailure)
        {
            return Result<ConsoleStartupOptions>.Fail($"Failed to create the shell history folder '{historyFolder}'")
                .WithErrors(historyResult);
        }

        foreach (var file in ConsoleStartupFiles.Generate(useShellProfile))
        {
            var filePath = Path.Combine(modeFolder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            var readResult = await fileSystem.ReadAllTextAsync(filePath);
            if (readResult.IsSuccess &&
                readResult.Value == file.Content)
            {
                continue;
            }

            var folderResult = await fileSystem.CreateFolderAsync(Path.GetDirectoryName(filePath)!);
            if (folderResult.IsFailure)
            {
                return Result<ConsoleStartupOptions>.Fail($"Failed to create the folder for '{filePath}'")
                    .WithErrors(folderResult);
            }

            var writeResult = await fileSystem.WriteAllTextAsync(filePath, file.Content);
            if (writeResult.IsFailure)
            {
                return Result<ConsoleStartupOptions>.Fail($"Failed to write the start-up file '{filePath}'")
                    .WithErrors(writeResult);
            }
        }

        var options = new ConsoleStartupOptions(modeFolder, historyFolder, useShellProfile, UserZdotdir: null);
        return options;
    }
}
