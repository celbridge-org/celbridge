using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Builds the in-memory project tree from disk, deferring visibility filtering
/// to the workspace-scoped policy engine.
/// </summary>
/// <remarks>
/// Walks the project folder directly with Directory.GetDirectories / GetFiles,
/// hence the AllowDirectFileSystemAccess exemption.
/// </remarks>
[AllowDirectFileSystemAccess]
public sealed class ProjectTreeBuilder : IProjectTreeBuilder
{
    private readonly IFileIconService _fileIconService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ProjectTreeBuilder(IFileIconService fileIconService, IWorkspaceWrapper workspaceWrapper)
    {
        _fileIconService = fileIconService;
        _workspaceWrapper = workspaceWrapper;
    }

    public IFolderResource BuildTree(string projectFolderPath)
    {
        var root = new FolderResource(string.Empty, null);
        SynchronizeFolder(root, projectFolderPath, projectFolderPath);
        return root;
    }

    private void SynchronizeFolder(FolderResource folderResource, string folderPath, string projectFolderPath)
    {
        var policy = _workspaceWrapper.WorkspaceService.ResourcePolicy;

        var subFolderPaths = Directory.GetDirectories(folderPath).OrderBy(d => d).ToList();
        subFolderPaths.RemoveAll(path => ShouldFilter(path, projectFolderPath, isFolder: true, policy));

        var filePaths = Directory.GetFiles(folderPath).OrderBy(f => f).ToList();
        filePaths.RemoveAll(path => ShouldFilter(path, projectFolderPath, isFolder: false, policy));

        // Children rebuild from scratch on every call so stale TreeViewNode.Content
        // references do not survive a rapid undo/redo cycle.
        folderResource.Children.Clear();

        foreach (var subFolderPath in subFolderPaths)
        {
            var folderName = Path.GetFileName(subFolderPath);
            var childFolder = new FolderResource(folderName, folderResource);
            SynchronizeFolder(childFolder, subFolderPath, projectFolderPath);
            folderResource.AddChild(childFolder);
        }

        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            var fileExtension = Path.GetExtension(filePath).TrimStart('.');

            var getIconResult = _fileIconService.GetFileIconForExtension(fileExtension);
            var iconDefinition = getIconResult.IsSuccess
                ? getIconResult.Value
                : _fileIconService.DefaultFileIcon;

            folderResource.AddChild(new FileResource(fileName, folderResource, iconDefinition));
        }

        folderResource.Children = folderResource.Children
            .OrderBy(child => child is IFolderResource ? 0 : 1)
            .ThenBy(child => child.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool ShouldFilter(string fullPath, string projectFolderPath, bool isFolder, IResourcePolicy policy)
    {
        var keyResult = BuildProjectKey(fullPath, projectFolderPath);
        if (keyResult.IsFailure)
        {
            return true;
        }

        var policyResult = policy.Evaluate(keyResult.Value, ResourceAction.List, isFolder);
        return policyResult.IsFailure;
    }

    private static Result<ResourceKey> BuildProjectKey(string fullPath, string projectFolderPath)
    {
        var trimmedProjectPath = projectFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.StartsWith(trimmedProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ResourceKey>.Fail($"Path is outside the project folder: '{fullPath}'");
        }

        var relativePath = fullPath
            .Substring(trimmedProjectPath.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (string.IsNullOrEmpty(relativePath))
        {
            return ResourceKey.Empty;
        }

        if (!ResourceKey.TryCreate(relativePath, out var key))
        {
            return Result<ResourceKey>.Fail($"Invalid resource key derived from path: '{relativePath}'");
        }
        return key;
    }
}
