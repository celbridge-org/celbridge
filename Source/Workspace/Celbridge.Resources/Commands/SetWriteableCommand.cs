using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class SetWriteableCommand : CommandBase, ISetWriteableCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public bool Writeable { get; set; }

    public SetWriteableCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        // Writeable == true clears the ReadOnly flag (set = false).
        // Writeable == false sets the ReadOnly flag (set = true).
        return await resourceFileSystem.SetAttributesAsync(
            Resource,
            FileSystemAttributes.ReadOnly,
            set: !Writeable);
    }
}
