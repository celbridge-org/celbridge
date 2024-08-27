using Celbridge.Commands;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

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
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;
        Guard.IsNotNull(resourceRegistry);

        var getResult = resourceRegistry.GetResource(FileResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"File resource not found. {FileResource}");
        }

        var fileResource = getResult.Value as IFileResource;
        if (fileResource is null)
        {
            return Result.Fail($"Resource is not a file. {FileResource}");
        }

        // Todo: Open the file resource via the documents service.

        await Task.CompletedTask;

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
