using System.Text.RegularExpressions;
using Celbridge.Logging;
using Celbridge.Projects.Services;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Removes the local data that earlier versions kept in the project folder. Deletes the .celbridge folder and
/// removes the data-folder key from the project file. The project then starts with an empty data folder in the
/// application data folder.
/// </summary>
public sealed class DataFolderMigrationStep : IMigrationStep
{
    // Earlier versions kept a project's local data in this folder.
    private const string CelbridgeFolderName = ".celbridge";

    private const string DataFolderKey = "data-folder";

    private static readonly Regex CelbridgeHeaderRegex = new(@"^\s*\[\s*celbridge\s*\]\s*(#.*)?$");
    private static readonly Regex TableHeaderRegex = new(@"^\s*\[");
    private static readonly Regex DataFolderLineRegex = new($@"^\s*{DataFolderKey}\s*=");

    public SemanticVersion TargetVersion { get; } = new SemanticVersion(1, 5, 0);

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        await DeleteCelbridgeFolderAsync(context);

        return await RemoveDataFolderKeyAsync(context);
    }

    // If the folder can't be deleted, log the failure and continue. The folder stays in the project as an
    // ordinary folder.
    private static async Task DeleteCelbridgeFolderAsync(MigrationContext context)
    {
        var celbridgeFolderPath = Path.Combine(context.ProjectFolderPath, CelbridgeFolderName);

        var infoResult = await context.FileSystem.GetInfoAsync(celbridgeFolderPath);
        var folderExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder;
        if (!folderExists)
        {
            return;
        }

        var deleteResult = await context.FileSystem.DeleteFolderAsync(celbridgeFolderPath, recursive: true);
        if (deleteResult.IsFailure)
        {
            context.Logger.LogWarning(deleteResult, $"Failed to delete the project's {CelbridgeFolderName} folder: '{celbridgeFolderPath}'");
            return;
        }

        context.Logger.LogInformation($"Deleted the project's {CelbridgeFolderName} folder: '{celbridgeFolderPath}'");
    }

    // Removes the key's line from the [celbridge] table and leaves the rest of the file unchanged.
    private static async Task<Result> RemoveDataFolderKeyAsync(MigrationContext context)
    {
        var hasDataFolderKey = JsonPointerToml.TryResolve(context.Configuration, $"/celbridge/{DataFolderKey}", out _, out _);
        if (!hasDataFolderKey)
        {
            return Result.Ok();
        }

        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read the project file to remove its {DataFolderKey} key")
                .WithErrors(readResult);
        }

        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
        var lines = text.Split('\n').ToList();

        var inCelbridgeTable = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (TableHeaderRegex.IsMatch(line))
            {
                inCelbridgeTable = CelbridgeHeaderRegex.IsMatch(line);
                continue;
            }

            if (inCelbridgeTable && DataFolderLineRegex.IsMatch(line))
            {
                lines.RemoveAt(index);
                index--;
            }
        }

        var writeResult = await context.WriteProjectFileAsync(string.Join('\n', lines));
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to remove the {DataFolderKey} key from the project file")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
