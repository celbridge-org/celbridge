using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>
    /// Opens a document in the editor. By default the document is opened without
    /// activating it, so the user's current active tab is preserved. Use
    /// document_activate to bring a document to the foreground.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to open.</param>
    /// <param name="sectionIndex">Target editor section: 0 (left), 1 (center), 2 (right). Use -1 to open in the active section (default).</param>
    /// <param name="forceReload">Force reload even if already open.</param>
    /// <param name="activate">When true, the opened document becomes the active tab.</param>
    /// <returns>"ok" on success, or an error message if the operation failed.</returns>
    [McpServerTool(Name = "document_open", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.open")]
    public async partial Task<CallToolResult> Open(string fileResource, int sectionIndex = -1, bool forceReload = false, bool activate = false)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        if (sectionIndex != -1 && sectionIndex is < 0 or > 2)
        {
            return ErrorResult($"Invalid sectionIndex '{sectionIndex}': must be 0, 1, 2, or -1 for the active section.");
        }

        int? targetSectionIndex = sectionIndex == -1 ? null : sectionIndex;

        return await ExecuteCommandAsync<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.TargetSectionIndex = targetSectionIndex;
            command.ForceReload = forceReload;
            command.Activate = activate;
        });
    }
}
