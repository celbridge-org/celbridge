using Celbridge.Settings;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for package operations: archiving, unarchiving, and workshop package management.
/// </summary>
[McpServerToolType]
public partial class PackageTools : AgentToolBase
{
    private ILogger<PackageTools>? _logger;

    public PackageTools(IApplicationServiceProvider services) : base(services) { }

    private ILogger<PackageTools> Logger => _logger ??= GetRequiredService<ILogger<PackageTools>>();

    private static string InvalidPackageNameError(string packageName)
    {
        return $"Invalid package name: '{packageName}'. " +
            $"Package names must be lowercase alphanumeric with single hyphen separators, 1-{PackageConstants.MaxNameLength} characters.";
    }

    // The 'latest' alias is server-managed, so the curation tools refuse to set
    // or remove it. Other aliases follow the conservative package-name rule.
    private static Result ValidateAlias(string alias)
    {
        if (string.Equals(alias, PackageConstants.LatestAlias, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail("The 'latest' alias is managed by the workshop and cannot be set or removed manually.");
        }

        if (!PackageName.IsValid(alias))
        {
            return Result.Fail(
                $"Invalid alias: '{alias}'. " +
                $"Aliases must be lowercase alphanumeric with single hyphen separators, 1-{PackageConstants.MaxNameLength} characters.");
        }

        return Result.Ok();
    }

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
        var editorSettings = GetRequiredService<IEditorSettings>();
        var author = editorSettings.WorkshopAuthor.Trim();
        if (author.Length > 0)
        {
            return author;
        }

        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var message = localizerService.GetString("Workshop_PublishBlocked_Message");
        if (confirmWithUser)
        {
            var title = localizerService.GetString("Workshop_PublishBlocked_Title");
            await ShowAlertAsync(title, message);
        }

        return Result<string>.Fail(message);
    }
}
