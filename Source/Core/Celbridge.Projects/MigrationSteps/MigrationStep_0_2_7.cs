using Celbridge.Projects.Services;
using Celbridge.Resources;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Migrates projects to v0.2.7. The .webapp file extension was renamed to .webview.
/// This step renames every *.webapp file in the project tree and rewrites any quoted
/// .webapp paths in the project TOML. Workspace settings (open tabs, recent files) are
/// ephemeral and intentionally not migrated; the worst case is that a tab fails to reopen.
/// </summary>
public class MigrationStep_0_2_7 : IMigrationStep
{
    private const string OldExtension = ".webapp";
    private const string NewExtension = ".webview";

    public Version TargetVersion => new Version("0.2.7");

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var renameResult = await RenameProjectFilesAsync(context);
        if (renameResult.IsFailure)
        {
            return renameResult;
        }

        var configResult = await RewriteProjectConfigAsync(context);
        if (configResult.IsFailure)
        {
            return configResult;
        }

        return Result.Ok();
    }

    /// <summary>
    /// Walks the project folder recursively and renames every *.webapp file to *.webview.
    /// Skips the project metadata folder so internal data files are left untouched.
    /// </summary>
    private async Task<Result> RenameProjectFilesAsync(MigrationContext context)
    {
        var projectDataFolderPath = Path.GetFullPath(context.ProjectDataFolderPath);

        var enumerateResult = await context.FileSystem.EnumerateAsync(
            context.ProjectFolderPath,
            $"*{OldExtension}",
            recursive: true);

        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate '{OldExtension}' files in project folder")
                .WithErrors(enumerateResult);
        }

        int renamedCount = 0;
        foreach (var oldPath in enumerateResult.Value.Where(entry => !entry.IsFolder).Select(entry => entry.FullPath))
        {
            var fullOldPath = Path.GetFullPath(oldPath);

            // Skip anything inside the metadata folder. Metadata uses its own format and
            // its contents are ephemeral, so any stale references regenerate on next use.
            if (fullOldPath.StartsWith(projectDataFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var newPath = Path.ChangeExtension(fullOldPath, NewExtension);

            // Defensive: an existing target file would imply a partially completed migration
            // or a manual rename. Leave both files in place and surface the conflict.
            var existingInfo = await context.FileSystem.GetInfoAsync(newPath);
            if (existingInfo.IsSuccess && existingInfo.Value.Kind == StorageItemKind.File)
            {
                return Result.Fail(
                    $"Cannot rename '{fullOldPath}' to '{newPath}'. Target file already exists.");
            }

            var moveResult = await context.FileSystem.MoveFileAsync(fullOldPath, newPath);
            if (moveResult.IsFailure)
            {
                return Result.Fail($"Failed to rename '{fullOldPath}' to '{newPath}'")
                    .WithErrors(moveResult);
            }

            renamedCount++;
        }

        if (renamedCount > 0)
        {
            context.Logger.LogInformation($"Renamed {renamedCount} '{OldExtension}' file(s) to '{NewExtension}'");
        }

        return Result.Ok();
    }

    /// <summary>
    /// Rewrites quoted .webapp paths in the project TOML file. Only string values that end with
    /// .webapp followed by the closing quote are rewritten; bare or unquoted occurrences are left
    /// alone to avoid mangling unrelated text.
    /// </summary>
    private async Task<Result> RewriteProjectConfigAsync(MigrationContext context)
    {
        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read project config: '{context.ProjectFilePath}'")
                .WithErrors(readResult);
        }

        var originalText = readResult.Value;

        var updatedText = originalText
            .Replace($"{OldExtension}\"", $"{NewExtension}\"")
            .Replace($"{OldExtension}'", $"{NewExtension}'");

        if (updatedText == originalText)
        {
            return Result.Ok();
        }

        var writeResult = await context.WriteProjectFileAsync(updatedText);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write rewritten project config: '{context.ProjectFilePath}'")
                .WithErrors(writeResult);
        }

        context.Logger.LogInformation(
            $"Rewrote '{OldExtension}' references in project config: '{context.ProjectFilePath}'");

        return Result.Ok();
    }
}
