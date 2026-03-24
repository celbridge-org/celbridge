using Celbridge.DataTransfer;
using Celbridge.Explorer;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for managing project resources (files and folders).
/// </summary>
[McpServerToolType]
public partial class ResourceTools : AgentToolBase
{
    public ResourceTools(IApplicationServiceProvider services) : base(services) {}

    /// <summary>
    /// Deletes a resource from the project.
    /// </summary>
    /// <param name="resource">Resource key of the item to delete.</param>
    /// <param name="confirm">Show a confirmation dialog before deleting.</param>
    [McpServerTool(Name = "resource_delete", Destructive = true)]
    [ToolAlias("resource.delete")]
    public async partial Task<CallToolResult> Delete(string resource, bool confirm = true)
    {
        if (confirm)
        {
            return await ExecuteCommandAsync<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resource };
            });
        }
        else
        {
            return await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resource };
            });
        }
    }

    /// <summary>
    /// Moves or renames a resource.
    /// </summary>
    /// <param name="sourceResource">Resource key of the source item.</param>
    /// <param name="destinationResource">Resource key of the destination.</param>
    [McpServerTool(Name = "resource_move", ReadOnly = false)]
    [ToolAlias("resource.move")]
    public async partial Task<CallToolResult> Move(string sourceResource, string destinationResource)
    {
        return await ExecuteCommandAsync<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResource };
            command.DestResource = destinationResource;
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
