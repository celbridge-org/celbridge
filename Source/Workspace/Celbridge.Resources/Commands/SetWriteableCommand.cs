using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class SetWriteableCommand : CommandBase, ISetWriteableCommand
{
    // UpdateResources triggers a registry refresh after the attribute flip so
    // the tree-cached IResource.WritableState matches the new on-disk state.
    // Without it, the writable-state cache in LocalResourceFileSystem treats
    // a freshly-locked file as still writable on the next write and strips
    // the read-only attribute back off before writing.
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

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
