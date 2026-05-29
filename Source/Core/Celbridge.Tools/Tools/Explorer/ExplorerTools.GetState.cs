using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by explorer_get_state with the visual state of the explorer panel.
/// </summary>
public record class ExplorerStateResult(
    string SelectedResource,
    List<string> SelectedResources,
    List<string> ExpandedFolders);

public partial class ExplorerTools
{
    /// <summary>Explorer panel state: selected resource(s) and expanded folders.</summary>
    [McpServerTool(Name = "explorer_get_state", ReadOnly = true)]
    [ToolAlias("explorer.get_state")]
    [RelatedGuides("workspace_panels")]
    public partial CallToolResult GetState()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var explorerService = workspaceWrapper.WorkspaceService.ExplorerService;

        // Empty resource keys (no selection) serialise as the empty string
        // rather than the canonical "project:" form, so the response field is
        // a clean signal that nothing is selected.
        var selectedResource = explorerService.SelectedResource;
        var selectedResourceString = selectedResource.IsEmpty ? string.Empty : selectedResource.ToString();

        var result = new ExplorerStateResult(
            selectedResourceString,
            explorerService.SelectedResources.Select(r => r.ToString()).ToList(),
            explorerService.FolderStateService.ExpandedFolders);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
