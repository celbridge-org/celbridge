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
    /// True when the path points to an existing file that can be opened for
    /// shared read access. Returns false for empty paths, missing files, or
    /// access-denied conditions.
    /// </summary>
    public bool CanAccessFile(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath)
            || !File.Exists(resourcePath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(resourcePath);
            using var stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves a resource key to its backing path and verifies the file
    /// exists and is readable. Returns the resolved path on success.
    /// </summary>
    public Result<string> ResolveAndValidateFilePath(ResourceKey fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File path does not exist: '{filePath}'");
        }

        if (!CanAccessFile(filePath))
        {
            return Result.Fail($"File exists but cannot be opened: '{filePath}'");
        }

        return filePath;
    }
}
