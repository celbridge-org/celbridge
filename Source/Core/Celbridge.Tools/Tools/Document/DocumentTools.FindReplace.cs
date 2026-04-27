using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_find_replace with the number of replacements made.
/// </summary>
public record class FindReplaceResult(int ReplacementCount);

public partial class DocumentTools
{
    /// <summary>
    /// Finds and replaces text within a document. Supports plain text and regex patterns.
    /// Multi-line search and replace text may use \n line endings regardless of the file's
    /// actual line endings — the tool normalises them automatically. Replacements are
    /// written directly to disk. Any open document reloads its buffer from disk after
    /// the write.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to perform find and replace on.</param>
    /// <param name="searchText">The text to search for.</param>
    /// <param name="replaceText">The replacement text.</param>
    /// <param name="matchCase">If true, the search is case-sensitive.</param>
    /// <param name="useRegex">If true, the search text is treated as a regular expression.</param>
    /// <param name="fromLine">First line number (1-based, inclusive) to include in the replacement scope. Zero (default) means no lower bound.</param>
    /// <param name="toLine">Last line number (1-based, inclusive) to include in the replacement scope. Zero (default) means no upper bound.</param>
    /// <returns>JSON object with field: replacementCount (int).</returns>
    [McpServerTool(Name = "document_find_replace")]
    [ToolAlias("document.find_replace")]
    public async partial Task<CallToolResult> FindReplace(
        string fileResource,
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool useRegex = false,
        int fromLine = 0,
        int toLine = 0)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        var (callResult, replacementCount) = await ExecuteCommandAsync<IFindReplaceDocumentCommand, int>(command =>
        {
            command.FileResource = fileResourceKey;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.UseRegex = useRegex;
            command.FromLine = fromLine;
            command.ToLine = toLine;
        });

        if (callResult.IsError == true)
        {
            return callResult;
        }

        var result = new FindReplaceResult(replacementCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
