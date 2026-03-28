using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for explorer panel operations: structural changes and navigation.
/// </summary>
[McpServerToolType]
public partial class ExplorerTools : AgentToolBase
{
    public ExplorerTools(IApplicationServiceProvider services) : base(services) { }

    /// <summary>
    /// Gets the currently selected resources in the explorer panel.
    /// </summary>
    /// <returns>JSON object with fields: anchor (string, the primary selected resource key), selected (array of string resource keys).</returns>
    [McpServerTool(Name = "explorer_get_selection", ReadOnly = true)]
    [ToolAlias("explorer.get_selection")]
    public partial string GetSelection()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var explorerService = workspaceWrapper.WorkspaceService.ExplorerService;

        var anchorResource = explorerService.SelectedResource;
        var selectedResources = explorerService.SelectedResources;

        return JsonSerializer.Serialize(new
        {
            anchor = anchorResource.ToString(),
            selected = selectedResources.Select(r => r.ToString()).ToList()
        });
    }

    /// <summary>
    /// Creates an empty file in the project. Pass show_dialog=true for interactive mode where the user can choose the name and location.
    /// </summary>
    /// <param name="resource">Resource key for the new file, or the parent folder when using the dialog.</param>
    /// <param name="showDialog">If true, show the create file dialog for interactive naming.</param>
    [McpServerTool(Name = "explorer_create_file")]
    [ToolAlias("explorer.create_file")]
    public async partial Task<CallToolResult> CreateFile(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            return await ExecuteCommandAsync<IAddResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.File;
                command.DestFolderResource = resourceKey;
            });
        }

        return await ExecuteCommandAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestResource = resourceKey;
        });
    }

    /// <summary>
    /// Creates an empty folder in the project. Pass show_dialog=true for interactive mode where the user can choose the name and location.
    /// </summary>
    /// <param name="resource">Resource key for the new folder, or the parent folder when using the dialog.</param>
    /// <param name="showDialog">If true, show the create folder dialog for interactive naming.</param>
    [McpServerTool(Name = "explorer_create_folder")]
    [ToolAlias("explorer.create_folder")]
    public async partial Task<CallToolResult> CreateFolder(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            return await ExecuteCommandAsync<IAddResourceDialogCommand>(command =>
            {
                command.ResourceType = ResourceType.Folder;
                command.DestFolderResource = resourceKey;
            });
        }

        return await ExecuteCommandAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestResource = resourceKey;
        });
    }

    /// <summary>
    /// Copies a resource to a new location in the project.
    /// </summary>
    /// <param name="sourceResource">Resource key of the source item.</param>
    /// <param name="destinationResource">Resource key of the destination.</param>
    [McpServerTool(Name = "explorer_copy")]
    [ToolAlias("explorer.copy")]
    public async partial Task<CallToolResult> Copy(string sourceResource, string destinationResource)
    {
        if (!ResourceKey.TryCreate(sourceResource, out var sourceResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{sourceResource}'");
        }
        if (!ResourceKey.TryCreate(destinationResource, out var destinationResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{destinationResource}'");
        }

        return await ExecuteCommandAsync<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    /// <summary>
    /// Moves or renames a resource.
    /// </summary>
    /// <param name="sourceResource">Resource key of the source item.</param>
    /// <param name="destinationResource">Resource key of the destination.</param>
    [McpServerTool(Name = "explorer_move")]
    [ToolAlias("explorer.move")]
    public async partial Task<CallToolResult> Move(string sourceResource, string destinationResource)
    {
        if (!ResourceKey.TryCreate(sourceResource, out var sourceResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{sourceResource}'");
        }
        if (!ResourceKey.TryCreate(destinationResource, out var destinationResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{destinationResource}'");
        }

        return await ExecuteCommandAsync<ICopyResourceCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.DestResource = destinationResourceKey;
            command.TransferMode = DataTransferMode.Move;
        });
    }

    /// <summary>
    /// Deletes a resource from the project. Pass show_dialog=true for a confirmation dialog.
    /// </summary>
    /// <param name="resource">Resource key of the item to delete.</param>
    /// <param name="showDialog">If true, show a delete confirmation dialog.</param>
    [McpServerTool(Name = "explorer_delete", Destructive = true)]
    [ToolAlias("explorer.delete")]
    public async partial Task<CallToolResult> Delete(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (showDialog)
        {
            return await ExecuteCommandAsync<IDeleteResourceDialogCommand>(command =>
            {
                command.Resources = new List<ResourceKey> { resourceKey };
            });
        }

        return await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
    }

    /// <summary>
    /// Shows the rename dialog for a resource. Renaming is always interactive.
    /// </summary>
    /// <param name="resource">Resource key of the item to rename.</param>
    [McpServerTool(Name = "explorer_rename")]
    [ToolAlias("explorer.rename")]
    public async partial Task<CallToolResult> Rename(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        return await ExecuteCommandAsync<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    /// <summary>
    /// Duplicates a resource. Pass show_dialog=true for interactive mode where the user can choose the new name.
    /// </summary>
    /// <param name="resource">Resource key of the item to duplicate.</param>
    /// <param name="showDialog">If true, show the duplicate dialog for interactive naming.</param>
    [McpServerTool(Name = "explorer_duplicate")]
    [ToolAlias("explorer.duplicate")]
    public async partial Task<CallToolResult> Duplicate(string resource, bool showDialog = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        return await ExecuteCommandAsync<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    /// <summary>
    /// Undoes the most recent explorer operation.
    /// </summary>
    [McpServerTool(Name = "explorer_undo")]
    [ToolAlias("explorer.undo")]
    public async partial Task<CallToolResult> Undo()
    {
        return await ExecuteCommandAsync<IUndoResourceCommand>();
    }

    /// <summary>
    /// Redoes the most recently undone explorer operation.
    /// </summary>
    [McpServerTool(Name = "explorer_redo")]
    [ToolAlias("explorer.redo")]
    public async partial Task<CallToolResult> Redo()
    {
        return await ExecuteCommandAsync<IRedoResourceCommand>();
    }

    /// <summary>
    /// Selects a resource in the explorer panel.
    /// </summary>
    /// <param name="resource">Resource key of the item to select.</param>
    /// <param name="showExplorerPanel">Show the explorer panel if hidden.</param>
    [McpServerTool(Name = "explorer_select", ReadOnly = true, Idempotent = true)]
    [ToolAlias("explorer.select")]
    public async partial Task<CallToolResult> Select(string resource, bool showExplorerPanel = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        return await ExecuteCommandAsync<ISelectResourceCommand>(command =>
        {
            command.Resource = resourceKey;
            command.ShowExplorerPanel = showExplorerPanel;
        });
    }

    /// <summary>
    /// Expands or collapses a folder in the explorer tree.
    /// </summary>
    /// <param name="resource">Resource key of the folder.</param>
    /// <param name="expanded">If true, expand the folder. If false, collapse it.</param>
    [McpServerTool(Name = "explorer_expand_folder")]
    [ToolAlias("explorer.expand_folder")]
    public async partial Task<CallToolResult> ExpandFolder(string resource, bool expanded = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        return await ExecuteCommandAsync<IExpandFolderCommand>(command =>
        {
            command.FolderResource = resourceKey;
            command.Expanded = expanded;
        });
    }

    /// <summary>
    /// Collapses all expanded folders in the explorer tree.
    /// </summary>
    [McpServerTool(Name = "explorer_collapse_all")]
    [ToolAlias("explorer.collapse_all")]
    public async partial Task<CallToolResult> CollapseAll()
    {
        return await ExecuteCommandAsync<ICollapseAllCommand>();
    }
}
