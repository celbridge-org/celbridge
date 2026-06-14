using Celbridge.Credentials;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for workshop page operations: publishing, listing, inspecting, and unpublishing static pages.
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
        var credentialService = GetRequiredService<ICredentialService>();
        var authorResult = await PublishAuthor.ResolveAsync(credentialService);
        if (authorResult.IsFailure)
        {
            if (confirmWithUser)
            {
                var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
                var title = localizerService.GetString("Workshop_PublishBlocked_Title");
                await ShowAlertAsync(title, authorResult.FirstErrorMessage);
            }

            return authorResult;
        }

        return authorResult.Value;
    }
}
