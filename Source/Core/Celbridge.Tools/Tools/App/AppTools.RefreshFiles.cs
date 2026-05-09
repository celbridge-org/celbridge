using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Rescan the project tree; only needed after non-Celbridge tools wrote files directly.</summary>
    [McpServerTool(Name = "app_refresh_files", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.refresh_files")]
    public partial CallToolResult RefreshFiles()
    {
        const string ToolGuide = "app_refresh_files";

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var result = workspaceWrapper.WorkspaceService.ResourceService.UpdateResources();
        if (result.IsFailure)
        {
            var failure = Result.Fail("Failed to refresh file listing")
                .WithErrors(result);
            return ToolResponse.Error(failure, ToolGuide);
        }

        return ToolResponse.Success("File listing refreshed.");
    }
}
