using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by explorer_get_context with the visual state of the explorer panel.
/// </summary>
public record class ExplorerContextResult(
    string SelectedResource,
    List<string> SelectedResources,
    List<string> ExpandedFolders);

public partial class ExplorerTools
{
    /// <summary>
    /// Gets the visual state of the explorer panel: the selected resources and the
    /// expanded folders. Use this to understand what the user is currently looking at
    /// in the file tree.
    /// </summary>
    /// <returns>JSON object with fields: selectedResource (string, the primary selected resource key), selectedResources (array of string resource keys), expandedFolders (array of string resource keys).</returns>
    [McpServerTool(Name = "explorer_get_context", ReadOnly = true)]
    [ToolAlias("explorer.get_context")]
    public partial CallToolResult GetContext()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var explorerService = workspaceWrapper.WorkspaceService.ExplorerService;

        var result = new ExplorerContextResult(
            explorerService.SelectedResource.ToString(),
            explorerService.SelectedResources.Select(r => r.ToString()).ToList(),
            explorerService.FolderStateService.ExpandedFolders);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
