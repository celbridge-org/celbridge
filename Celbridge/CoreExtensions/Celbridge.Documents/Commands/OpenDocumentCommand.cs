using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class OpenDocumentCommand : CommandBase, IOpenDocumentCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    public ResourceKey FileResource { get; set; }

    public OpenDocumentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var openResult = await documentsService.OpenDocument(FileResource);
        if (openResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to open file resource '{FileResource}'");
            failure.MergeErrors(openResult);
            return failure;
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void OpenDocument(ResourceKey fileResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }
}
