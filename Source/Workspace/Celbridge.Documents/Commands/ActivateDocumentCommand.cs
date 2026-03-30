using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ActivateDocumentCommand : CommandBase, IActivateDocumentCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    public ActivateDocumentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var activateResult = documentsService.ActivateDocument(FileResource);
        if (activateResult.IsFailure)
        {
            return Result.Fail($"Failed to activate document for file resource '{FileResource}'")
                .WithErrors(activateResult);
        }

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void ActivateDocument(ResourceKey fileResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IActivateDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }
}
