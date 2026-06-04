using System.Text.Json;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for reading, querying, and modifying .xlsx workbooks. Reads use
/// ISpreadsheetReader directly against a stream opened through the resource
/// file system. Writes route through ISpreadsheet*Command implementations so
/// they appear in the command audit trail.
/// </summary>
[McpServerToolType]
public partial class SpreadsheetTools : AgentToolBase
{
    private const string XlsxExtension = ".xlsx";

    public SpreadsheetTools(IApplicationServiceProvider services) : base(services) { }

    // Validates the resource is a present .xlsx file and returns its key.
    // Mirrors SpreadsheetHelper.ResolveWorkbookResourceAsync in the
    // Spreadsheet module — that helper is internal to its assembly so the
    // tool layer reimplements the same check rather than taking a module
    // dependency.
    private async Task<Result<ResourceKey>> ResolveWorkbookResourceAsync(string resource)
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
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var infoResult = await resourceFileSystem.GetInfoAsync(resourceKey);
        if (infoResult.IsFailure)
        {
            return Result.Fail($"Failed to inspect workbook: '{resourceKey}'")
                .WithErrors(infoResult);
        }

        var info = infoResult.Value;
        if (info.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"File not found: '{resourceKey}'");
        }
        if (info.Kind != StorageItemKind.File)
        {
            return Result.Fail($"Resource is not a file: '{resourceKey}'");
        }

        return resourceKey;
    }

    // Opens a read-only stream on the workbook via the file storage gateway.
    // Caller disposes.
    private async Task<Result<Stream>> OpenWorkbookStreamAsync(ResourceKey resource)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var openResult = await resourceFileSystem.OpenReadAsync(resource);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open workbook: '{resource}'")
                .WithErrors(openResult);
        }

        return openResult.Value;
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
