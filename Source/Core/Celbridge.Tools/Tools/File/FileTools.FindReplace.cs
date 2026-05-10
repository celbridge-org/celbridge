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
    /// <summary>Replace literal or regex matches inside one file, optionally scoped to a line range.</summary>
    [McpServerTool(Name = "file_find_replace")]
    [ToolAlias("file.find_replace")]
    [RelatedGuides("resource_keys", "regex_syntax", "editing_documents", "file_changes")]
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
            return ToolResponse.InvalidResourceKey(fileResource);
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
            return ToolResponse.Error(findReplaceResult);
        }

        var replacementCount = findReplaceResult.Value;
        var result = new FindReplaceResult(replacementCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
