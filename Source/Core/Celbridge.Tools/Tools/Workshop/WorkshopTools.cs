using Celbridge.Localization;
using Celbridge.Settings;
using Celbridge.Workshop;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools that publish packages and pages to the workshop and install packages from it.
/// </summary>
[McpServerToolType]
public partial class WorkshopTools : AgentToolBase
{
    private ILogger<WorkshopTools>? _logger;

    public WorkshopTools(IApplicationServiceProvider services) : base(services) { }

    private ILogger<WorkshopTools> Logger => _logger ??= GetRequiredService<ILogger<WorkshopTools>>();

    private static string InvalidPackageNameError(string packageName)
    {
        return $"Invalid package name: '{packageName}'. " +
            $"Package names must be lowercase alphanumeric with single hyphen separators, 1-{PackageConstants.MaxNameLength} characters.";
    }

    // 'latest' is reserved for the highest live workshop version, and the workshop
    // never accepts it as an alias name, so the curation tools refuse to set or
    // remove it. Other aliases follow the package-name rule.
    private static Result ValidateAlias(string alias)
    {
        if (string.Equals(alias, WorkshopConstants.LatestAlias, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail("'latest' is reserved for the highest live workshop version and is never an alias, so it cannot be set or removed.");
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
