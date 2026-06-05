using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Builds the in-memory project tree by enumerating the project root through the
/// resource file-system gateway. Visibility filtering lives in the gateway's
/// policy evaluation, so the builder no longer touches the file system directly
/// or applies its own filters.
/// </summary>
public sealed class ProjectTreeBuilder : IProjectTreeBuilder
{
    private readonly IFileIconService _fileIconService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ProjectTreeBuilder(
        IFileIconService fileIconService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _fileIconService = fileIconService;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result<IFolderResource>> BuildTreeAsync()
    {
        var root = new FolderResource(string.Empty, null);

        var synchronizeResult = await SynchronizeFolderAsync(root, ResourceKey.Empty);
        if (synchronizeResult.IsFailure)
        {
            return Result<IFolderResource>.Fail("Failed to build the project tree.")
                .WithErrors(synchronizeResult);
        }

        return Result<IFolderResource>.Ok(root);
    }

    private async Task<Result> SynchronizeFolderAsync(FolderResource folderResource, ResourceKey folderKey)
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceFileSystem = resourceService.FileSystem;
        var policy = resourceService.Policy;
        var rootHandlerRegistry = resourceService.RootHandlers;

        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(folderKey);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate folder: '{folderKey}'")
                .WithErrors(enumerateResult);
        }
        var folderItems = enumerateResult.Value;

        // Children rebuild from scratch on every call so stale TreeViewNode.Content
        // references do not survive a rapid undo/redo cycle.
        folderResource.Children.Clear();

        foreach (var folderItem in folderItems)
        {
            var childName = folderItem.Resource.ResourceName;
            var writableState = ComputeWritableState(folderItem, policy, rootHandlerRegistry);

            if (folderItem.IsFolder)
            {
                var childFolder = new FolderResource(childName, folderResource);
                childFolder.WritableState = writableState;

                var childResult = await SynchronizeFolderAsync(childFolder, folderItem.Resource);
                if (childResult.IsFailure)
                {
                    return childResult;
                }

                folderResource.AddChild(childFolder);
            }
            else
            {
                var fileExtension = GetFileExtension(childName);

                var getIconResult = _fileIconService.GetFileIconForExtension(fileExtension);
                var iconDefinition = getIconResult.IsSuccess
                    ? getIconResult.Value
                    : _fileIconService.DefaultFileIcon;

                var fileResource = new FileResource(childName, folderResource, iconDefinition);
                fileResource.WritableState = writableState;
                folderResource.AddChild(fileResource);
            }
        }

        // EnumerateFolderAsync yields folders-first, ordinal order, which matches
        // the tree's required ordering, so no re-sort is needed here.
        return Result.Ok();
    }

    // Priority mirrors IResourceOperationService.GetWritableStateAsync:
    // Locked > ReadOnlyRoot > ReadOnlyAttribute. Configured locks dominate
    // ambient state so a locked file on a read-only root reports Locked.
    private static WritableState ComputeWritableState(
        FolderItem folderItem,
        IResourcePolicy policy,
        IRootHandlerRegistry rootHandlerRegistry)
    {
        var writeResult = policy.Evaluate(folderItem.Resource, ResourceAction.Write, folderItem.IsFolder);
        if (writeResult.IsFailure)
        {
            return WritableState.Locked;
        }

        if (rootHandlerRegistry.RootHandlers.TryGetValue(folderItem.Resource.Root, out var handler)
            && !handler.Capabilities.IsWritable)
        {
            return WritableState.ReadOnlyRoot;
        }

        if ((folderItem.Attributes & FileSystemAttributes.ReadOnly) != 0)
        {
            return WritableState.ReadOnlyAttribute;
        }

        return WritableState.Writable;
    }

    private static string GetFileExtension(string fileName)
    {
        int lastDotIndex = fileName.LastIndexOf('.');
        if (lastDotIndex < 0)
        {
            return string.Empty;
        }

        return fileName.Substring(lastDotIndex + 1);
    }
}
