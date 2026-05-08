using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Forces a refresh of the project file listing. Only needed when non-Celbridge MCP tools wrote files directly.
    /// </summary>
    /// <returns>"ok" on success.</returns>
    [McpServerTool(Name = "app_refresh_files", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.refresh_files")]
    public partial CallToolResult RefreshFiles()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var result = workspaceWrapper.WorkspaceService.ResourceService.UpdateResources();
        if (result.IsFailure)
        {
            var failure = Result.Fail("Failed to refresh file listing")
                .WithErrors(result);
            return ToolError(failure);
        }

        return ToolSuccess("File listing refreshed.");
    }
}
