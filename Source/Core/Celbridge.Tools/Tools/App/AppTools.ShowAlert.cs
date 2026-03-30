using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>
    /// Shows an alert dialog to the user with a message and optional title.
    /// </summary>
    /// <param name="message">The message to display in the alert dialog.</param>
    /// <param name="title">Optional title for the alert dialog.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "app_show_alert")]
    [ToolAlias("app.show_alert")]
    public async partial Task<CallToolResult> ShowAlert(string message, string title = "")
    {
        return await ExecuteCommandAsync<IAlertCommand>(command =>
        {
            command.Message = message;
            command.Title = title;
        });
    }
}
