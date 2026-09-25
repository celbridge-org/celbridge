using System.Reflection;
using System.Text;

namespace Celbridge.Projects;

/// <summary>
/// Writes the .gitignore that Celbridge maintains for new projects. A project folder that already has
/// one keeps it, gaining only the patterns it was missing, so an existing repository's rules survive.
/// </summary>
public static class GitIgnoreWriter
{
    private const string GitIgnoreFileName = ".gitignore";
    private const string GitIgnoreResourceName = "Celbridge.Projects.Assets.GitIgnore.txt";

    // Labels the patterns appended to a .gitignore the project folder already had.
    private const string AppendedBlockHeader = "# Added by Celbridge";

    /// <summary>
    /// Writes Celbridge's .gitignore into the project folder, merging into the file already there
    /// rather than replacing it.
    /// </summary>
    public static async Task<Result> WriteAsync(string projectFolderPath, ILocalFileSystem fileSystem)
    {
        Guard.IsNotNullOrWhiteSpace(projectFolderPath);

        var celbridgeContents = ReadCelbridgeGitIgnore();
        var gitIgnorePath = Path.Combine(projectFolderPath, GitIgnoreFileName);

        var existingInfo = await fileSystem.GetInfoAsync(gitIgnorePath);
        var hasExistingGitIgnore = existingInfo.IsSuccess
            && existingInfo.Value.Kind == StorageItemKind.File;

        if (!hasExistingGitIgnore)
        {
            return await fileSystem.WriteAllTextAsync(gitIgnorePath, celbridgeContents);
        }

        var readResult = await fileSystem.ReadAllTextAsync(gitIgnorePath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read existing .gitignore: {gitIgnorePath}")
                .WithErrors(readResult);
        }

        var existingContents = readResult.Value;
        var mergedContents = Merge(existingContents, celbridgeContents);
        if (mergedContents is null)
        {
            return Result.Ok();
        }

        return await fileSystem.WriteAllTextAsync(gitIgnorePath, mergedContents);
    }

    /// <summary>
    /// Returns the existing contents with Celbridge's missing patterns appended under a marker, or
    /// null when the existing contents already declare every one of them.
    /// </summary>
    public static string? Merge(string existingContents, string celbridgeContents)
    {
        var existingPatterns = new HashSet<string>(ReadPatterns(existingContents), StringComparer.Ordinal);

        var missingPatterns = new List<string>();
        foreach (var pattern in ReadPatterns(celbridgeContents))
        {
            if (existingPatterns.Add(pattern))
            {
                missingPatterns.Add(pattern);
            }
        }

        if (missingPatterns.Count == 0)
        {
            return null;
        }

        return Append(existingContents, missingPatterns);
    }

    // Comparison is ordinal because a gitignore pattern is literal text that git matches
    // case-sensitively, unlike a filesystem path.
    private static List<string> ReadPatterns(string contents)
    {
        var patterns = new List<string>();
        var lines = contents.Split('\n');
        foreach (var line in lines)
        {
            var pattern = line.Trim();
            if (pattern.Length == 0
                || pattern.StartsWith('#'))
            {
                continue;
            }

            patterns.Add(pattern);
        }

        return patterns;
    }

    private static string Append(string existingContents, List<string> missingPatterns)
    {
        // Match whatever the existing file uses, so merging into a CRLF file doesn't mix endings.
        var lineEnding = existingContents.Contains("\r\n") ? "\r\n" : "\n";

        var builder = new StringBuilder(existingContents);
        if (existingContents.Length > 0)
        {
            if (!existingContents.EndsWith('\n'))
            {
                builder.Append(lineEnding);
            }

            // A blank line separates the appended block from the rules already there.
            builder.Append(lineEnding);
        }

        builder.Append(AppendedBlockHeader);
        builder.Append(lineEnding);
        foreach (var pattern in missingPatterns)
        {
            builder.Append(pattern);
            builder.Append(lineEnding);
        }

        return builder.ToString();
    }

    private static string ReadCelbridgeGitIgnore()
    {
        var assembly = typeof(GitIgnoreWriter).Assembly;
        using var stream = assembly.GetManifestResourceStream(GitIgnoreResourceName)
            ?? throw new InvalidDataException($"Resource '{GitIgnoreResourceName}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
