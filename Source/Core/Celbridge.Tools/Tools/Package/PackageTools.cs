using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for package operations: archiving, unarchiving, and workshop package management.
/// </summary>
[McpServerToolType]
public partial class PackageTools : AgentToolBase
{
    public PackageTools(IApplicationServiceProvider services) : base(services) { }

    private static string InvalidPackageNameError(string packageName)
    {
        return $"Invalid package name: '{packageName}'. " +
            $"Package names must be lowercase alphanumeric with single hyphen separators, 1-{PackageConstants.MaxNameLength} characters.";
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
}
