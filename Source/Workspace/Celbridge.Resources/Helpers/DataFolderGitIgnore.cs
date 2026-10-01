namespace Celbridge.Resources.Helpers;

/// <summary>
/// Keeps the project data folder out of version control with its own .gitignore, whatever the project's root
/// .gitignore says. The folder holds machine-specific state and the shell and IPython histories. The histories
/// can contain anything the user typed.
/// </summary>
public static class DataFolderGitIgnore
{
    private const string GitIgnoreFileName = ".gitignore";
    private const string IgnoreEverything = "*\n";

    /// <summary>
    /// Writes the data folder's .gitignore unless the folder already has one.
    /// </summary>
    public static async Task<Result> EnsureAsync(ILocalFileSystem fileSystem, string dataFolderPath)
    {
        var gitIgnorePath = Path.Combine(dataFolderPath, GitIgnoreFileName);

        var infoResult = await fileSystem.GetInfoAsync(gitIgnorePath);
        var hasGitIgnore = infoResult.IsSuccess &&
            infoResult.Value.Kind == StorageItemKind.File;
        if (hasGitIgnore)
        {
            return Result.Ok();
        }

        return await fileSystem.WriteAllTextAsync(gitIgnorePath, IgnoreEverything);
    }
}
