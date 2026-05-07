using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for package operations: archiving, unarchiving, and package registry management.
/// </summary>
[McpServerToolType]
public partial class PackageTools : AgentToolBase
{
    public PackageTools(IApplicationServiceProvider services) : base(services) { }

    private static bool IsValidPackageName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 214)
        {
            return false;
        }

        return Regex.IsMatch(name, @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$") &&
               !name.Contains("--");
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
