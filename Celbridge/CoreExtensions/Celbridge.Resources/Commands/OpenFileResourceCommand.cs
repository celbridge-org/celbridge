using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class OpenFileResourceCommand : CommandBase, IOpenFileResourceCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    public ResourceKey FileResource { get; set; }

    public OpenFileResourceCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var openResult = await documentsService.OpenFileDocument(FileResource);
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
    public static void OpenFile(ResourceKey fileResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IOpenFileResourceCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }
}
