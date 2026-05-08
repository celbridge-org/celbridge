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
    /// <summary>Explorer panel state: selected resource(s) and expanded folders.</summary>
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
        return ToolSuccess(json);
    }
}
