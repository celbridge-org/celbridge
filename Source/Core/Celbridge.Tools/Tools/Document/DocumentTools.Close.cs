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
    /// <summary>
    /// Closes one or more documents. Closes are sequential; a failure does not stop the remaining attempts.
    /// </summary>
    /// <param name="fileResource">Resource key, or a JSON array of resource keys.</param>
    /// <param name="forceClose">Force close without save confirmation.</param>
    /// <returns>JSON object with closed, failed, and errors.</returns>
    [McpServerTool(Name = "document_close", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.close")]
    public async partial Task<CallToolResult> Close(string fileResource, bool forceClose = false)
    {
        var resourceKeyStrings = ParseResourceKeys(fileResource);

        var validatedKeys = new List<ResourceKey>();
        foreach (var keyString in resourceKeyStrings)
        {
            if (!ResourceKey.TryCreate(keyString, out var validatedKey))
            {
                return ToolError($"Invalid resource key: '{keyString}'");
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
            return ToolError(json);
        }

        return ToolSuccess(json);
    }
}
