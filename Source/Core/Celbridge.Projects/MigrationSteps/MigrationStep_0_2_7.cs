using Celbridge.Projects.Services;

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
        var renameResult = RenameProjectFiles(context);
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
    private Result RenameProjectFiles(MigrationContext context)
    {
        try
        {
            var projectDataFolderPath = Path.GetFullPath(context.ProjectDataFolderPath);

            var matches = Directory.EnumerateFiles(
                context.ProjectFolderPath,
                $"*{OldExtension}",
                SearchOption.AllDirectories);

            int renamedCount = 0;
            foreach (var oldPath in matches)
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
                if (File.Exists(newPath))
                {
                    return Result.Fail(
                        $"Cannot rename '{fullOldPath}' to '{newPath}'. Target file already exists.");
                }

                File.Move(fullOldPath, newPath);
                renamedCount++;
            }

            if (renamedCount > 0)
            {
                context.Logger.LogInformation($"Renamed {renamedCount} '{OldExtension}' file(s) to '{NewExtension}'");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to rename '{OldExtension}' files in project folder")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Rewrites quoted .webapp paths in the project TOML file. Only string values that end with
    /// .webapp followed by the closing quote are rewritten; bare or unquoted occurrences are left
    /// alone to avoid mangling unrelated text.
    /// </summary>
    private async Task<Result> RewriteProjectConfigAsync(MigrationContext context)
    {
        try
        {
            var originalText = await File.ReadAllTextAsync(context.ProjectFilePath);

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
        catch (Exception ex)
        {
            return Result.Fail("Failed to rewrite project config")
                .WithException(ex);
        }
    }
}
