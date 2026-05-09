using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Show a modal alert dialog (always interactive; blocks until the user dismisses it).</summary>
    [McpServerTool(Name = "app_show_alert")]
    [ToolAlias("app.show_alert")]
    public async partial Task<CallToolResult> ShowAlert(string message, string title = "")
    {
        const string ToolGuide = "app_show_alert";

        var alertResult = await ExecuteCommandAsync<IAlertCommand>(command =>
        {
            command.Message = message;
            command.Title = title;
        });
        if (alertResult.IsFailure)
        {
            return ToolResponse.Error(alertResult, ToolGuide);
        }

        return ToolResponse.Success("ok");
    }
}
