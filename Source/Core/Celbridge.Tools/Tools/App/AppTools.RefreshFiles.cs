using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Forces an immediate refresh of the project file listing.
    /// Only needed when non-Celbridge MCP tools have written files to the project folder directly.
    /// Celbridge tools always keep the file listing up to date automatically.
    /// </summary>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "app_refresh_files", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.refresh_files")]
    public partial CallToolResult RefreshFiles()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var result = workspaceWrapper.WorkspaceService.ResourceService.UpdateResources();
        if (result.IsFailure)
        {
            return ErrorResult($"Failed to refresh file listing: {result.FirstErrorMessage}");
        }

        return SuccessResult("File listing refreshed.");
    }
}
