using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class CloseDocumentCommand : CommandBase, ICloseDocumentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

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
            var failure = Result.Fail($"Failed to close document for file resource '{FileResource}'");
            failure.MergeErrors(closeResult);
            return failure;
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
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceClose = forceClose;
        });
    }
}
