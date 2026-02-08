using Celbridge.Commands;
using Celbridge.Workspace;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Explorer.Commands;

public class CopyFilePathCommand : CommandBase, ICopyFilePathCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey ResourceKey { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CopyFilePathCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        if (ResourceKey.IsEmpty)
        {
            return Result.Fail("Resource key is empty");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var filePath = Path.Combine(resourceRegistry.ProjectFolderPath, ResourceKey.ToString());

        var dataPackage = new DataPackage();
        dataPackage.SetText(filePath);
        Clipboard.SetContent(dataPackage);

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyFilePath(ResourceKey resourceKey)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyFilePathCommand>(command =>
        {
            command.ResourceKey = resourceKey;
        });
    }
}

