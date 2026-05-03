using System.Text.Json;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for reading, querying, and modifying .xlsx workbooks. Reads use
/// ISpreadsheetReader directly. Writes route through ISpreadsheet*Command
/// implementations so they appear in the command audit trail.
/// </summary>
[McpServerToolType]
public partial class SpreadsheetTools : AgentToolBase
{
    private const string XlsxExtension = ".xlsx";

    public SpreadsheetTools(IApplicationServiceProvider services) : base(services) { }

    private Result<string> ResolveWorkbookPath(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return Result<string>.Fail($"Invalid resource key: '{resource}'");
        }

        var extension = Path.GetExtension(resource);
        if (!string.Equals(extension, XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Fail($"Resource is not an .xlsx workbook: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to resolve path for resource: '{resource}'");
        }
        var workbookPath = resolveResult.Value;

        if (!File.Exists(workbookPath))
        {
            return Result<string>.Fail($"File not found: '{resource}'");
        }

        return Result<string>.Ok(workbookPath);
    }

    private static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
