using System.Reflection;
using System.Text;

namespace Celbridge.Projects;

/// <summary>
/// Writes the .gitignore that Celbridge maintains for new projects. A project folder that already has
/// one keeps it: Celbridge's patterns sit in a marked block, and the rest of the file is left alone.
/// </summary>
public static class GitIgnoreWriter
{
    private const string GitIgnoreFileName = ".gitignore";
    private const string GitIgnoreResourceName = "Celbridge.Projects.Assets.GitIgnore.txt";

    // Opens the run of patterns Celbridge maintains inside a .gitignore the project folder already
    // had. The run reaches to the next blank line, and later writes rewrite it in place.
    private const string BlockHeader = "# Added by Celbridge";

    /// <summary>
    /// Writes Celbridge's .gitignore into the project folder, merging into the file already there
    /// rather than replacing it.
    /// </summary>
    public static async Task<Result> WriteAsync(string projectFolderPath, ILocalFileSystem fileSystem)
    {
        try
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
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when writing the .gitignore: {projectFolderPath}")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Returns the existing contents with Celbridge's block of patterns brought up to date, or null
    /// when they already are. Patterns the file declares outside the block are left to it.
    /// </summary>
    public static string? Merge(string existingContents, string celbridgeContents)
    {
        var celbridgePatterns = ReadPatterns(celbridgeContents);

        // Split on the newline alone so each line keeps any carriage return it came with, and the
        // lines this merge does not touch survive it byte for byte.
        var lines = new List<string>(existingContents.Split('\n'));

        var blockStartIndex = FindBlockStart(lines);
        if (blockStartIndex < 0)
        {
            return AppendBlock(existingContents, celbridgePatterns);
        }

        var blockEndIndex = FindBlockEnd(lines, blockStartIndex);
        var mergedContents = RewriteBlock(existingContents, lines, blockStartIndex, blockEndIndex, celbridgePatterns);

        return mergedContents == existingContents ? null : mergedContents;
    }

    private static int FindBlockStart(List<string> lines)
    {
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (lines[lineIndex].Trim() == BlockHeader)
            {
                return lineIndex;
            }
        }

        return -1;
    }

    private static int FindBlockEnd(List<string> lines, int blockStartIndex)
    {
        for (var lineIndex = blockStartIndex + 1; lineIndex < lines.Count; lineIndex++)
        {
            if (lines[lineIndex].Trim().Length == 0)
            {
                return lineIndex;
            }
        }

        return lines.Count;
    }

    private static string RewriteBlock(
        string existingContents,
        List<string> lines,
        int blockStartIndex,
        int blockEndIndex,
        List<string> celbridgePatterns)
    {
        var declaredPatterns = ReadPatternsOutsideBlock(lines, blockStartIndex, blockEndIndex);
        var celbridgePatternSet = new HashSet<string>(celbridgePatterns, StringComparer.Ordinal);

        // Lines inside the block that Celbridge does not ship were put there by hand, so they stay.
        var keptLines = new List<string>();
        for (var lineIndex = blockStartIndex + 1; lineIndex < blockEndIndex; lineIndex++)
        {
            var line = lines[lineIndex];
            if (celbridgePatternSet.Contains(line.Trim()))
            {
                continue;
            }

            keptLines.Add(line);
        }

        var lineSuffix = existingContents.Contains("\r\n") ? "\r" : string.Empty;

        var blockLines = new List<string>();
        foreach (var pattern in celbridgePatterns)
        {
            if (declaredPatterns.Contains(pattern))
            {
                continue;
            }

            blockLines.Add(pattern + lineSuffix);
        }

        blockLines.AddRange(keptLines);

        lines.RemoveRange(blockStartIndex, blockEndIndex - blockStartIndex);
        if (blockLines.Count > 0)
        {
            blockLines.Insert(0, BlockHeader + lineSuffix);
            lines.InsertRange(blockStartIndex, blockLines);
        }
        else
        {
            // Nothing left for the header to label, so the blank line that set it apart goes too.
            RemoveBlankLineBefore(lines, blockStartIndex);
        }

        return string.Join('\n', lines);
    }

    // Patterns the file declares for itself. The block does not repeat these, so a rule the user
    // moved out of it stays out.
    private static HashSet<string> ReadPatternsOutsideBlock(List<string> lines, int blockStartIndex, int blockEndIndex)
    {
        var declaredPatterns = new HashSet<string>(StringComparer.Ordinal);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var isInsideBlock = lineIndex >= blockStartIndex
                && lineIndex < blockEndIndex;

            if (isInsideBlock)
            {
                continue;
            }

            var pattern = lines[lineIndex].Trim();
            if (IsPattern(pattern))
            {
                declaredPatterns.Add(pattern);
            }
        }

        return declaredPatterns;
    }

    private static void RemoveBlankLineBefore(List<string> lines, int lineIndex)
    {
        var blankLineIndex = lineIndex - 1;
        var hasBlankLineBefore = blankLineIndex >= 0
            && lines[blankLineIndex].Trim().Length == 0;

        if (hasBlankLineBefore)
        {
            lines.RemoveAt(blankLineIndex);
        }
    }

    // Adds the block to a file that has never had one.
    private static string? AppendBlock(string existingContents, List<string> celbridgePatterns)
    {
        var declaredPatterns = new HashSet<string>(ReadPatterns(existingContents), StringComparer.Ordinal);

        var missingPatterns = new List<string>();
        foreach (var pattern in celbridgePatterns)
        {
            if (declaredPatterns.Contains(pattern))
            {
                continue;
            }

            missingPatterns.Add(pattern);
        }

        if (missingPatterns.Count == 0)
        {
            return null;
        }

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

        builder.Append(BlockHeader);
        builder.Append(lineEnding);
        foreach (var pattern in missingPatterns)
        {
            builder.Append(pattern);
            builder.Append(lineEnding);
        }

        return builder.ToString();
    }

    // Comparison is ordinal because a gitignore pattern is literal text that git matches
    // case-sensitively, unlike a filesystem path.
    private static List<string> ReadPatterns(string contents)
    {
        var patterns = new List<string>();
        var seenPatterns = new HashSet<string>(StringComparer.Ordinal);

        var lines = contents.Split('\n');
        foreach (var line in lines)
        {
            var pattern = line.Trim();
            if (!IsPattern(pattern))
            {
                continue;
            }

            if (seenPatterns.Add(pattern))
            {
                patterns.Add(pattern);
            }
        }

        return patterns;
    }

    private static bool IsPattern(string trimmedLine)
    {
        return trimmedLine.Length > 0
            && !trimmedLine.StartsWith('#');
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
