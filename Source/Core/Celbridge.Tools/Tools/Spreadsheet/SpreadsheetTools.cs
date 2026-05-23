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
            return Result.Fail($"Invalid resource key: '{resource}'");
        }

        var extension = Path.GetExtension(resource);
        if (!string.Equals(extension, XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail($"Resource is not an .xlsx workbook: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (!File.Exists(workbookPath))
        {
            return Result.Fail($"File not found: '{resourceKey}'");
        }

        return workbookPath;
    }

    private static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Unwraps a JsonElement into a JSON-typed CLR object: null, bool, double,
    /// or string. Nested objects and arrays fall back to their JSON text
    /// representation. The spreadsheet write surface only accepts scalars in
    /// cell positions.
    /// </summary>
    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            _ => element.GetRawText()
        };
    }
}
