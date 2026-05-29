using System.Text.Json;
using Celbridge.DataTransfer;
using Celbridge.Explorer;
using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class ExplorerTools
{
    /// <summary>Duplicate a resource in place; silent by default (auto-generates a unique name).</summary>
    [McpServerTool(Name = "explorer_duplicate")]
    [ToolAlias("explorer.duplicate")]
    [RelatedGuides("resource_keys", "undo_semantics")]
    public async partial Task<CallToolResult> Duplicate(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (showDialog)
        {
            var dialogResult = await ExecuteCommandAsync<IDuplicateResourceDialogCommand>(command =>
            {
                command.Resource = resourceKey;
            });
            if (dialogResult.IsFailure)
            {
                return ToolResponse.Error(dialogResult);
            }
            return ToolResponse.Success("ok");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return ToolResponse.Error("Workspace is not loaded.");
        }
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resourceKey);
        if (getResult.IsFailure)
        {
            return ToolResponse.Error($"Cannot duplicate resource '{resourceKey}': resource does not exist.");
        }

        var destKeyResult = ResourceNameHelper.GenerateUniqueDuplicateKey(resourceKey, resourceRegistry);
        if (destKeyResult.IsFailure)
        {
            return ToolResponse.Error(destKeyResult);
        }
        var destResource = destKeyResult.Value;

        // Issue Copy directly rather than wrapping it in another command that
        // would have to await it from inside its executor. The command queue
        // is single-threaded; a command's body awaiting another command via
        // ExecuteAsync deadlocks the queue.
        var copyResult = await ExecuteCommandAsync<ICopyResourceCommand, CopyCommandResult>(command =>
        {
            command.SourceResources = new List<ResourceKey> { resourceKey };
            command.DestResource = destResource;
            command.TransferMode = DataTransferMode.Copy;
        });
        if (copyResult.IsFailure)
        {
            return ToolResponse.Error(copyResult);
        }

        var payload = new
        {
            status = "ok",
            createdResource = destResource.ToString(),
        };
        return ToolResponse.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
