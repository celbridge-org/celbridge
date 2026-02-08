using Celbridge.Commands;
using Celbridge.Workspace;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Explorer.Commands;

public class CopyResourceKeyCommand : CommandBase, ICopyResourceKeyCommand
{
    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey ResourceKey { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CopyResourceKeyCommand(IWorkspaceWrapper workspaceWrapper)
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

        var dataPackage = new DataPackage();
        dataPackage.SetText(ResourceKey.ToString());
        Clipboard.SetContent(dataPackage);

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyResourceKey(ResourceKey resourceKey)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceKeyCommand>(command =>
        {
            command.ResourceKey = resourceKey;
        });
    }
}

