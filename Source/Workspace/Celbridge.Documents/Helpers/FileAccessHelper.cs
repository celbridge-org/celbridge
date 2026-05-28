using Celbridge.Workspace;

namespace Celbridge.Documents.Helpers;

/// <summary>
/// Resolves resource keys to backing file paths and verifies that the file
/// exists and is readable. Used by the documents subsystem to gate opens and
/// restores on access checks without scattering File.IO calls.
/// </summary>
public class FileAccessHelper
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public FileAccessHelper(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    /// <summary>
    /// True when the resource key resolves to an existing file that can be
    /// opened for shared read access. Returns false for missing files or
    /// access-denied conditions.
    /// </summary>
    public async Task<bool> CanAccessFileAsync(ResourceKey fileResource)
    {
        var fileStorage = _workspaceWrapper.WorkspaceService.FileStorage;

        var infoResult = await fileStorage.GetInfoAsync(fileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return false;
        }

        var openResult = await fileStorage.OpenReadAsync(fileResource);
        if (openResult.IsFailure)
        {
            return false;
        }
        openResult.Value.Dispose();
        return true;
    }

    /// <summary>
    /// Resolves a resource key to its backing path and verifies the file
    /// exists and is readable. Returns the resolved path on success.
    /// </summary>
    public async Task<Result<string>> ResolveAndValidateFilePathAsync(ResourceKey fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        var fileStorage = _workspaceWrapper.WorkspaceService.FileStorage;
        var infoResult = await fileStorage.GetInfoAsync(fileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"File path does not exist: '{filePath}'");
        }

        if (!await CanAccessFileAsync(fileResource))
        {
            return Result.Fail($"File exists but cannot be opened: '{filePath}'");
        }

        return filePath;
    }
}
