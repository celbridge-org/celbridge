using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>Open a document in the editor (without activating it by default).</summary>
    [McpServerTool(Name = "document_open", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.open")]
    public async partial Task<CallToolResult> Open(string fileResource, int sectionIndex = -1, bool forceReload = false, bool activate = false)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolError($"Invalid resource key: '{fileResource}'");
        }

        if (sectionIndex != -1 && sectionIndex is < 0 or > 2)
        {
            return ToolError($"Invalid sectionIndex '{sectionIndex}': must be 0, 1, 2, or -1 for the active section.");
        }

        int? targetSectionIndex = sectionIndex == -1 ? null : sectionIndex;

        var openResult = await ExecuteCommandAsync<IOpenDocumentCommand, OpenDocumentOutcome>(command =>
        {
            command.FileResource = fileResourceKey;
            command.TargetSectionIndex = targetSectionIndex;
            command.ForceReload = forceReload;
            command.Activate = activate;
        });

        if (openResult.IsFailure)
        {
            return ToolError(openResult);
        }

        var outcome = openResult.Value;
        return outcome == OpenDocumentOutcome.Cancelled
            ? ToolSuccess("cancelled")
            : ToolSuccess("opened");
    }
}
