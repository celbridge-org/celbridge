using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Project.Commands;

public class PasteResourceFromClipboardCommand : CommandBase, IPasteResourceFromClipboardCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override string UndoStackName => UndoStackNames.None;

    public ResourceKey FolderResource { get; set; }

    public PasteResourceFromClipboardCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var clipboardService = _workspaceWrapper.WorkspaceService.ClipboardService;
        if (clipboardService.GetClipboardContentType() != ClipboardContentType.Resource)
        {
            return Result.Fail("Clipboard does not contain a resource to paste");
        }

        return await clipboardService.PasteResourceItems(FolderResource);
    }

    //
    // Static methods for scripting support.
    //

    public static void PasteResourceFromClipboard(ResourceKey folderResource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.FolderResource = folderResource;
        });
    }
}
