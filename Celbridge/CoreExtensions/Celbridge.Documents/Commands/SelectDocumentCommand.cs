using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class SelectDocumentCommand : CommandBase, ISelectDocumentCommand
{
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
            var failure = Result.Fail($"Failed to select document for file resource '{FileResource}'");
            failure.MergeErrors(selectResult);
            return failure;
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
