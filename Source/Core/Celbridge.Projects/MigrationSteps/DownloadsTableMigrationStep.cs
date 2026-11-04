using System.Text.RegularExpressions;
using Celbridge.Logging;
using Celbridge.Projects.Services;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Removes the [celbridge.downloads] table from the project file. Earlier versions read the table's folder key to
/// choose where to save downloads. The step removes the whole table, whatever keys it has, and the rest of the file
/// stays the same.
/// </summary>
public sealed class DownloadsTableMigrationStep : IMigrationStep
{
    private const string DownloadsTableName = "celbridge.downloads";

    private static readonly Regex TableHeaderRegex = new(@"^\s*\[");
    private static readonly Regex DownloadsHeaderRegex = new(@"^\s*\[\s*celbridge\s*\.\s*downloads\s*\]\s*(#.*)?$");
    private static readonly Regex CommentLineRegex = new(@"^\s*#");

    public SemanticVersion TargetVersion { get; } = new SemanticVersion(1, 7, 0);

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var hasDownloadsTable = JsonPointerToml.TryResolve(context.Configuration, "/celbridge/downloads", out _, out _);
        if (!hasDownloadsTable)
        {
            return Result.Ok();
        }

        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read the project file to remove its [{DownloadsTableName}] table")
                .WithErrors(readResult);
        }

        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
        var endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline)
        {
            text = text[..^1];
        }
        var lines = text.Split('\n').ToList();

        // A downloads key written as an inline table or as dotted keys has no header to remove. Loading the project
        // reports the key as unknown and drops it.
        if (!RemoveDownloadsTable(lines))
        {
            return Result.Ok();
        }

        var migratedText = string.Join('\n', lines);
        if (endsWithNewline)
        {
            migratedText += '\n';
        }

        var writeResult = await context.WriteProjectFileAsync(migratedText);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to remove the [{DownloadsTableName}] table from the project file")
                .WithErrors(writeResult);
        }

        context.Logger.LogInformation($"Removed the [{DownloadsTableName}] table from the project file");

        return Result.Ok();
    }

    // Removes the table's header, the table's keys and the comment lines directly above the header. The comment
    // lines directly above the next header stay with the next table. Returns false when the file has no
    // [celbridge.downloads] header.
    private static bool RemoveDownloadsTable(List<string> lines)
    {
        var headerIndex = lines.FindIndex(line => DownloadsHeaderRegex.IsMatch(line));
        if (headerIndex < 0)
        {
            return false;
        }

        var startIndex = headerIndex;
        while (startIndex > 0
            && CommentLineRegex.IsMatch(lines[startIndex - 1]))
        {
            startIndex--;
        }

        var endIndex = FindNextTableHeader(lines, headerIndex + 1);
        if (endIndex < lines.Count)
        {
            while (endIndex > headerIndex + 1
                && CommentLineRegex.IsMatch(lines[endIndex - 1]))
            {
                endIndex--;
            }
        }

        lines.RemoveRange(startIndex, endIndex - startIndex);

        // Remove the blank lines left at the end of the file when the table was the last one.
        if (startIndex == lines.Count)
        {
            while (lines.Count > 0
                && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }

        return true;
    }

    private static int FindNextTableHeader(List<string> lines, int startIndex)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (TableHeaderRegex.IsMatch(lines[index]))
            {
                return index;
            }
        }

        return lines.Count;
    }
}
