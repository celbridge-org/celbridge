using Celbridge.Projects;
using Celbridge.UserInterface;

namespace Celbridge.Resources.Services;

/// <summary>
/// Builds the in-memory project tree from disk, applying built-in folder and
/// file visibility filters along the way.
/// </summary>
/// <remarks>
/// Uses File.GetAttributes directly because the gateway does not surface the
/// Windows hidden flag.
/// </remarks>
[AllowDirectFileSystemAccess]
public sealed class ProjectTreeBuilder : IProjectTreeBuilder
{
    private readonly IFileIconService _fileIconService;

    public ProjectTreeBuilder(IFileIconService fileIconService)
    {
        _fileIconService = fileIconService;
    }

    public IFolderResource BuildTree(string projectFolderPath)
    {
        var root = new FolderResource(string.Empty, null);
        SynchronizeFolder(root, projectFolderPath);
        return root;
    }

    private void SynchronizeFolder(FolderResource folderResource, string folderPath)
    {
        bool isProjectFolder = folderResource.ParentFolder is null;
        var subFolderPaths = Directory.GetDirectories(folderPath).OrderBy(d => d).ToList();
        RemoveHiddenFolders(subFolderPaths, isProjectFolder);

        var filePaths = Directory.GetFiles(folderPath).OrderBy(f => f).ToList();
        RemoveHiddenFiles(filePaths);

        // Children rebuild from scratch on every call so stale TreeViewNode.Content
        // references do not survive a rapid undo/redo cycle.
        folderResource.Children.Clear();

        foreach (var subFolderPath in subFolderPaths)
        {
            var folderName = Path.GetFileName(subFolderPath);
            var childFolder = new FolderResource(folderName, folderResource);
            SynchronizeFolder(childFolder, subFolderPath);
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
            .ThenBy(child => child.Name)
            .ToList();
    }

    private static void RemoveHiddenFolders(List<string> folderPaths, bool isProjectFolder)
    {
        folderPaths.RemoveAll(path =>
        {
            if (Path.GetFileName(path).StartsWith('.'))
            {
                // Hidden by leading dot. Includes the .celbridge metadata folder.
                return true;
            }

#if WINDOWS
            var attributes = File.GetAttributes(path);
            if ((attributes & System.IO.FileAttributes.Hidden) != 0)
            {
                return true;
            }
#endif
            var dirInfo = new DirectoryInfo(path);

            if (isProjectFolder
                && dirInfo.Name == LegacyConstants.MetaDataFolder)
            {
                return true;
            }

            if (dirInfo.Name == "__pycache__")
            {
                return true;
            }

            // Python/Lib carries pip packages and is excluded so the user sees
            // their project, not their virtualenv internals.
            if (dirInfo.Name == "Lib"
                && dirInfo.Parent?.Name == "Python")
            {
                return true;
            }

            return false;
        });
    }

    private static void RemoveHiddenFiles(List<string> filePaths)
    {
        filePaths.RemoveAll(path =>
        {
            if (Path.GetFileName(path).StartsWith('.'))
            {
                return true;
            }

#if WINDOWS
            var attributes = File.GetAttributes(path);
            if ((attributes & System.IO.FileAttributes.Hidden) != 0)
            {
                return true;
            }
#endif

            return false;
        });
    }
}
