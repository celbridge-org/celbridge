using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_find_replace with the number of replacements made.
/// </summary>
public record class FindReplaceResult(int ReplacementCount);

public partial class FileTools
{
    /// <summary>
    /// Finds and replaces text within a file. Multi-line text may use \n line endings; the tool normalises to the file's actual endings.
    /// </summary>
    /// <param name="fileResource">Resource key of the file.</param>
    /// <param name="searchText">Text to search for.</param>
    /// <param name="replaceText">Replacement text.</param>
    /// <param name="matchCase">If true, the search is case-sensitive.</param>
    /// <param name="useRegex">If true, searchText is treated as a regular expression.</param>
    /// <param name="fromLine">First line (1-based, inclusive) of the replacement scope. 0 means no lower bound.</param>
    /// <param name="toLine">Last line (1-based, inclusive) of the replacement scope. 0 means no upper bound.</param>
    /// <returns>JSON object with replacementCount.</returns>
    [McpServerTool(Name = "file_find_replace")]
    [ToolAlias("file.find_replace")]
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
            return ToolError($"Invalid resource key: '{fileResource}'");
        }

        var findReplaceResult = await ExecuteCommandAsync<IFindReplaceFileCommand, int>(command =>
        {
            command.FileResource = fileResourceKey;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.UseRegex = useRegex;
            command.FromLine = fromLine;
            command.ToLine = toLine;
        });

        if (findReplaceResult.IsFailure)
        {
            return ToolError(findReplaceResult);
        }

        var replacementCount = findReplaceResult.Value;
        var result = new FindReplaceResult(replacementCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolSuccess(json);
    }
}
