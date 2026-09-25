using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Rescan the project tree; only needed after non-Celbridge tools wrote files directly.</summary>
    [McpServerTool(Name = "app_refresh_files", ReadOnly = false, Idempotent = true)]
    [ToolAlias("app.refresh_files")]
    [RelatedGuides]
    public async partial Task<CallToolResult> RefreshFiles()
    {
        var updateResult = await ExecuteCommandAsync<IUpdateResourcesCommand>(command =>
        {
            command.Immediate = true;
        });
        if (updateResult.IsFailure)
        {
            var failure = Result.Fail("Failed to refresh file listing")
                .WithErrors(updateResult);
            return ToolResponse.Error(failure);
        }

        return ToolResponse.Success("File listing refreshed.");
    }
}
