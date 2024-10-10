using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class SelectDocumentCommand : CommandBase, ISelectDocumentCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    public SelectDocumentCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var selectResult = documentsService.SelectDocument(FileResource);
        if (selectResult.IsFailure)
        {
            return Result.Fail($"Failed to select document for file resource '{FileResource}'")
                .AddErrors(selectResult);
        }

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void SelectDocument(ResourceKey fileResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ISelectDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }
}
