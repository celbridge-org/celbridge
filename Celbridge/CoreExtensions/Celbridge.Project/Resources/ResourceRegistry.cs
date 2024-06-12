using Celbridge.BaseLibrary.Resources;
using System.Collections.ObjectModel;

using Path = System.IO.Path;

namespace Celbridge.Project.Resources;

public class ResourceRegistry : IResourceRegistry
{
    private readonly string _projectFolder;
    private FolderResource _rootFolder = new(string.Empty);

    public ObservableCollection<IResource> Resources
    {
        get
        {
            return _rootFolder.Children;
        }
    }

    public ResourceRegistry(string projectFolder)
    {
        _projectFolder = projectFolder;
    }

    public Result UpdateRegistry()
    {
        var scanResult = ScanFolder(_projectFolder);
        if (scanResult.IsFailure)
        {
            return Result.Fail(scanResult.Error);
        }

        var newRootFolder = scanResult.Value;

        return UpdateFolderResource(_rootFolder, newRootFolder);
    }

    /// <summary>
    // Updates only the changed parts of the resource tree, leaving unmodified parts of the tree untouched.
    /// </summary>
    private Result UpdateFolderResource(FolderResource currentFolder, FolderResource newFolder)
    {
        try
        {
            bool isSortRequired = false;

            // Update files
            var currentFiles = currentFolder.Children.OfType<FileResource>().ToList();
            var newFiles = newFolder.Children.OfType<FileResource>().ToList();

            // Remove deleted files
            foreach (var file in currentFiles)
            {
                if (!newFiles.Any(f => f.Name == file.Name))
                {
                    currentFolder.Children.Remove(file);
                }
            }

            // Add new files
            foreach (var file in newFiles)
            {
                if (!currentFiles.Any(f => f.Name == file.Name))
                {
                    currentFolder.Children.Add(file);
                    isSortRequired = true;
                }
            }

            // Update folders
            var currentFolders = currentFolder.Children.OfType<FolderResource>().ToList();
            var newFolders = newFolder.Children.OfType<FolderResource>().ToList();

            foreach (var folder in currentFolders)
            {
                var correspondingNewFolder = newFolders.FirstOrDefault(f => f.Name == folder.Name);
                if (correspondingNewFolder == null)
                {
                    // Folder deleted
                    currentFolder.Children.Remove(folder);
                }
                else
                {
                    // Folder exists, update recursively
                    UpdateFolderResource(folder, correspondingNewFolder);
                }
            }

            // Add new folders
            foreach (var folder in newFolders)
            {
                if (!currentFolders.Any(f => f.Name == folder.Name))
                {
                    currentFolder.Children.Add(folder);
                    isSortRequired = true;
                }
            }

            if (isSortRequired)
            {
                // Sort resources so they are listed alphabetically, with folders appearing before files.
                // This is an in-place bubble sort, so it might struggle if N gets large.
                // Todo: Implement a more efficient in-place sort that minizes changes to the collection.
                //for (int i = 0; i < currentFolder.Children.Count - 1; i++)
                //{
                //    for (int j = i + 1; j < currentFolder.Children.Count; j++)
                //    {
                //        var resource1 = currentFolder.Children[i];
                //        var resource2 = currentFolder.Children[j];
                //        bool shouldSwap = (resource1 is FileResource && resource2 is FolderResource) ||
                //                          (resource1.GetType() == resource2.GetType() && string.Compare(resource1.Name, resource2.Name) > 0);
                //        if (shouldSwap)
                //        {
                //            currentFolder.Children.Move(j, i);
                //            currentFolder.Children.Move(i + 1, j);
                //        }
                //    }
                //}
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to update resource registry. {ex.Message}");
        }

        return Result.Ok();
    }

    private Result<FolderResource> ScanFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return Result<FolderResource>.Fail($"Failed to scan folder. Path not found '{path}'");
            }

            var folderResource = new FolderResource(Path.GetFileName(path));

            var sortedDirectories = Directory.GetDirectories(path).OrderBy(d => d).ToList();
            foreach (var directory in sortedDirectories)
            {
                var scanResult = ScanFolder(directory);
                if (scanResult.IsFailure)
                {
                    return scanResult;
                }
                var resource = scanResult.Value;

                folderResource.AddChild(resource);
            }

            var sortedFiles = Directory.GetFiles(path).OrderBy(f => f).ToList();
            foreach (var file in sortedFiles)
            {
                folderResource.AddChild(new FileResource(Path.GetFileName(file)));
            }

            return Result<FolderResource>.Ok(folderResource);
        }
        catch (Exception ex)
        {
            return Result<FolderResource>.Fail($"Failed to scan folder '{path}'. {ex.Message}");
        }
    }
}