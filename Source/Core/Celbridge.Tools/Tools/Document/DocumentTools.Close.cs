using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_close with a summary of closed and failed documents.
/// </summary>
public record class DocumentCloseResult(int Closed, int Failed, List<string> Errors);

public partial class DocumentTools
{
    /// <summary>Close one or more open documents; sequential, partial-success batch.</summary>
    [McpServerTool(Name = "document_close", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.close")]
    [RelatedGuides("resource_keys", "workspace_panels")]
    public async partial Task<CallToolResult> Close(string fileResource, bool forceClose = false)
    {
        var resourceKeyStrings = ParseResourceKeys(fileResource);

        var validatedKeys = new List<ResourceKey>();
        foreach (var keyString in resourceKeyStrings)
        {
            if (!ResourceKey.TryCreate(keyString, out var validatedKey))
            {
                return ToolResponse.InvalidResourceKey(keyString);
            }
            validatedKeys.Add(validatedKey);
        }

        int closedCount = 0;
        var errors = new List<string>();

        foreach (var resourceKey in validatedKeys)
        {
            var result = await CommandService.ExecuteAsync<ICloseDocumentCommand>(command =>
            {
                command.FileResource = resourceKey;
                command.ForceClose = forceClose;
            });

            if (result.IsSuccess)
            {
                closedCount++;
            }
            else
            {
                errors.Add(result.MessageChain);
            }
        }

        var summary = new DocumentCloseResult(closedCount, errors.Count, errors);
        var json = JsonSerializer.Serialize(summary, JsonOptions);

        if (errors.Count > 0)
        {
            return ToolResponse.Error(json);
        }

        return ToolResponse.Success(json);
    }
}
