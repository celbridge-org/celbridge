using Celbridge.Broker;
using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class CloseDocumentCommand : CommandBase, ICloseDocumentCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    public bool ForceClose { get; set; }

    public CloseDocumentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var closeResult = await documentsService.CloseDocument(FileResource, ForceClose);
        if (closeResult.IsFailure)
        {
            return Result.Fail($"Failed to close document for file resource '{FileResource}'")
                .WithErrors(closeResult);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CloseDocument(ResourceKey fileResource)
    {
        CloseDocument(fileResource, false);
    }

    public static void CloseDocument(ResourceKey fileResource, bool forceClose)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceClose = forceClose;
        });
    }

    //
    // Broker tool methods.
    //

    [McpTool(Name = "document/close", Alias = "close", Description = "Closes a document in the editor")]
    public static void BrokerCloseDocument(
        [McpParam(Description = "Resource key of the file to close")]
        ResourceKey fileResource,
        [McpParam(Description = "Force close without save confirmation")]
        bool forceClose = false)
    {
        CloseDocument(fileResource, forceClose);
    }
}
