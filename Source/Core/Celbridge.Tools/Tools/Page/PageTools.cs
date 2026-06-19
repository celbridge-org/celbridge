using Celbridge.Localization;
using Celbridge.Settings;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for workshop page operations.
/// </summary>
[McpServerToolType]
public partial class PageTools : AgentToolBase
{
    public PageTools(IApplicationServiceProvider services) : base(services) { }

    private async Task<bool> ConfirmActionAsync(string title, string message)
    {
        var confirmResultWrapper = await ExecuteCommandAsync<IConfirmActionCommand, ConfirmActionResult>(command =>
        {
            command.Title = title;
            command.Message = message;
        });

        if (confirmResultWrapper.IsFailure)
        {
            return false;
        }

        var confirmResult = confirmResultWrapper.Value;
        return confirmResult.Confirmed;
    }

    // Resolves the publisher Author from Workshop settings, alerting the user
    // (when interactive) if it is missing so the problem is visible and not just
    // returned to the agent.
    private async Task<Result<string>> ResolvePublishAuthorAsync(bool confirmWithUser)
    {
        var settingsService = GetRequiredService<ISettingsService>();
        var author = settingsService.Get(SettingCatalog.Workshop.Author).Trim();
        if (author.Length > 0)
        {
            return author;
        }

        var localizerService = GetRequiredService<ILocalizerService>();
        var message = localizerService.GetString("Workshop_PublishBlocked_Message");
        if (confirmWithUser)
        {
            var title = localizerService.GetString("Workshop_PublishBlocked_Title");
            await ShowAlertAsync(title, message);
        }

        return Result<string>.Fail(message);
    }
}
