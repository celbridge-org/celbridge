using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DocumentTools
{
    /// <summary>Open a document in the editor (without activating it by default).</summary>
    [McpServerTool(Name = "document_open", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.open")]
    [RelatedGuides("resource_keys", "workspace_panels")]
    public async partial Task<CallToolResult> Open(string fileResource, string section = "", bool forceReload = false, bool activate = false, int line = 0, int column = 0)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ToolResponse.InvalidResourceKey(fileResource);
        }

        if (line < 0 ||
            column < 0)
        {
            return ToolResponse.Error("Line and column must be one-based positions, or zero for none.");
        }

        DocumentSection? targetSection = null;
        if (!string.IsNullOrEmpty(section))
        {
            if (!DocumentSectionTokens.TryParse(section, out var parsedSection))
            {
                var sectionNames = string.Join(", ", DocumentSectionTokens.AllTokens);
                return ToolResponse.Error($"Invalid section '{section}': must be one of {sectionNames}, or empty to open in {DocumentLayoutHelper.DefaultOpenSection.ToToken()}.");
            }

            targetSection = parsedSection;
        }

        var location = DocumentLocation.Compose(line, column);

        var openResult = await ExecuteCommandAsync<IOpenDocumentCommand, OpenDocumentOutcome>(command =>
        {
            command.FileResource = fileResourceKey;
            command.TargetSection = targetSection;
            command.ForceReload = forceReload;
            command.Activate = activate;
            command.Location = location;
        });

        if (openResult.IsFailure)
        {
            return ToolResponse.Error(openResult);
        }

        var outcome = openResult.Value;
        return outcome == OpenDocumentOutcome.Cancelled
            ? ToolResponse.Success("cancelled")
            : ToolResponse.Success("opened");
    }
}
