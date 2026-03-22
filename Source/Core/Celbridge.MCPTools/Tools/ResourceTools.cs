using Celbridge.DataTransfer;
using Celbridge.Explorer;
using ModelContextProtocol.Server;

namespace Celbridge.MCPTools.Tools;

/// <summary>
/// MCP tools for managing project resources (files and folders).
/// </summary>
[McpServerToolType]
public class ResourceTools : ToolBase
{
    public ResourceTools(IApplicationServiceProvider services) : base(services) {}

    /// <summary>
    /// Deletes a resource.
    /// </summary>
    /// <param name="resource">Resource key of the item to delete.</param>
    /// <param name="confirm">Show a confirmation dialog before deleting.</param>
    [McpServerTool(Name = "resource_delete", Destructive = true)]
    [ToolAlias("delete")]
    public void Delete(string resource, bool confirm = true)
    {
        if (confirm)
        {
            CommandService.Execute<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resource };
            });
        }
        else
        {
            CommandService.Execute<IDeleteResourceCommand>(command =>
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
    [ToolAlias("move")]
    public void Move(string sourceResource, string destinationResource)
    {
        CommandService.Execute<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResource };
            command.DestResource = destinationResource;
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
