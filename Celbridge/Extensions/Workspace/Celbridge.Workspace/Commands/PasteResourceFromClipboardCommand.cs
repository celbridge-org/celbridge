using Celbridge.DataTransfer;
using Celbridge.Commands;

namespace Celbridge.Workspace.Commands;

public class PasteResourceFromClipboardCommand : CommandBase, IPasteResourceFromClipboardCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override string UndoStackName => UndoStackNames.None;

    public ResourceKey DestFolderResource { get; set; }

    public PasteResourceFromClipboardCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var dataTransferService = _workspaceWrapper.WorkspaceService.DataTransferService;

        var contentDescription = dataTransferService.GetClipboardContentDescription();

        if (contentDescription.ContentType != ClipboardContentType.Resource)
        {
            return Result.Fail("Clipboard does not contain a resource to paste");
        }

        return await dataTransferService.PasteClipboardResources(DestFolderResource);
    }

    //
    // Static methods for scripting support.
    //

    public static void PasteResourceFromClipboard(ResourceKey folderResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = folderResource;
        });
    }
}
