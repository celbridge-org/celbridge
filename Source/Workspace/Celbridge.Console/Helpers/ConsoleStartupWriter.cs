using Celbridge.FileSystem;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Writes the start-up files for a console's shell into the project data folder. A file is only written when it
/// is missing or its content differs, so after an upgrade only the changed files are rewritten.
/// </summary>
public static class ConsoleStartupWriter
{
    private const string ConsoleFolderName = "console";
    private const string HistoryFolderName = "history";

    /// <summary>
    /// Writes the shell's start-up files, creates the history folder, and returns both locations.
    /// </summary>
    public static async Task<Result<ConsoleStartupOptions>> WriteAsync(
        ILocalFileSystem fileSystem,
        string projectDataFolderPath,
        ConsoleShell shell)
    {
        var consoleFolder = Path.Combine(projectDataFolderPath, ConsoleFolderName);
        var historyFolder = Path.Combine(consoleFolder, HistoryFolderName);

        var historyResult = await fileSystem.CreateFolderAsync(historyFolder);
        if (historyResult.IsFailure)
        {
            return Result<ConsoleStartupOptions>.Fail($"Failed to create the shell history folder '{historyFolder}'")
                .WithErrors(historyResult);
        }

        foreach (var file in ConsoleStartupFiles.Generate(shell))
        {
            var filePath = Path.Combine(consoleFolder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (await HoldsContentAsync(fileSystem, filePath, file.Content))
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

        var options = new ConsoleStartupOptions(consoleFolder, historyFolder);
        return options;
    }

    // Whether the file already has this content. The file's existence is checked before it is read, because
    // reading a missing file throws an exception that stops the debugger. Every file is missing for a project's
    // first console.
    private static async Task<bool> HoldsContentAsync(ILocalFileSystem fileSystem, string filePath, string content)
    {
        var infoResult = await fileSystem.GetInfoAsync(filePath);
        var isFile = infoResult.IsSuccess &&
            infoResult.Value.Kind == StorageItemKind.File;
        if (!isFile)
        {
            return false;
        }

        var readResult = await fileSystem.ReadAllTextAsync(filePath);
        return readResult.IsSuccess &&
            readResult.Value == content;
    }
}
