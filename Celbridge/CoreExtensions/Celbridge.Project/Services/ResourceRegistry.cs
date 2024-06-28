using Celbridge.BaseLibrary.Resources;
using Celbridge.Project.Models;
using CommunityToolkit.Diagnostics;
using System.Collections.ObjectModel;
using System.Text;

namespace Celbridge.Project.Services;

public class ResourceRegistry : IResourceRegistry
{
    private readonly string _projectFolder;
    private FolderResource _rootFolder = new(string.Empty, null);

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

    public string GetResourcePath(IResource resource)
    {
        var pathResource = resource as Resource;
        Guard.IsNotNull(pathResource);

        var sb = new StringBuilder();
        void AddPathSegment(Resource resource)
        {
            if (resource.ParentFolder is null)
            {
                return;
            }

            // Build path by recursively visiting each parent folders
            AddPathSegment(resource.ParentFolder);

            // The trick is to append the path segment after we've visited the parent.
            // This ensures the path segments are appended in the right order.
            if (sb.Length > 0)
            {
                sb.Append("/");
            }
            sb.Append(resource.Name);
        }
        AddPathSegment(pathResource);

        var path = sb.ToString();

        return path;
    }

    public Result UpdateRegistry()
    {
        var scanResult = CreateFolderResource(_projectFolder);
        if (scanResult.IsFailure)
        {
            return Result.Fail(scanResult.Error);
        }

        var newRootFolder = scanResult.Value;

        UpdateFolderResource(_rootFolder, newRootFolder);

        return Result.Ok();
    }

    /// <summary>
    // Updates the changed parts of the resource tree, leaving unmodified parts of the tree untouched.
    /// </summary>
    private Result UpdateFolderResource(FolderResource currentFolder, FolderResource newFolder)
    {
        try
        {
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
                    // Insert file resource in sorted order
                    InsertResource(currentFolder.Children, file);
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
                    // Insert folder resource in sorted order
                    InsertResource(currentFolder.Children, folder);
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to update resource registry. {ex.Message}");
        }

        return Result.Ok();
    }

    private void InsertResource(ObservableCollection<IResource> collection, IResource resource)
    {
        int index = 0;
        while (index < collection.Count)
        {
            IResource item = collection[index];

            if (resource is FolderResource && item is not FolderResource)
            {
                // Folders appear before files
                break;
            }

            if (resource.GetType() == item.GetType() && string.Compare(resource.Name, item.Name, StringComparison.InvariantCulture) < 0)
            {
                // Alphabetical order
                break;
            }

            index++;
        }

        collection.Insert(index, resource);
    }

    private Result<FolderResource> CreateFolderResource(string path, FolderResource? parentFolder = null)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return Result<FolderResource>.Fail($"Failed to scan folder. Path not found '{path}'");
            }

            //
            // Create a new folder resource to represent the directory at this path
            //
            var newFolderResource = new FolderResource(Path.GetFileName(path), parentFolder);

            //
            // Create a new folder resource for each descendant folder and add it as a child of the new folder resource.
            //
            var sortedDirectories = Directory.GetDirectories(path).OrderBy(d => d).ToList();
            foreach (var directory in sortedDirectories)
            {
                var scanResult = CreateFolderResource(directory, newFolderResource);
                if (scanResult.IsFailure)
                {
                    return scanResult;
                }
                var resource = scanResult.Value;

                newFolderResource.AddChild(resource);
            }

            //
            // Create a new file resource for each descendent file and add it as a child of the new folder resource.
            //
            var sortedFiles = Directory.GetFiles(path).OrderBy(f => f).ToList();
            foreach (var file in sortedFiles)
            {
                var newFileResource = new FileResource(Path.GetFileName(file), newFolderResource);
                newFolderResource.AddChild(newFileResource);
            }

            return Result<FolderResource>.Ok(newFolderResource);
        }
        catch (Exception ex)
        {
            return Result<FolderResource>.Fail($"Failed to scan folder '{path}'. {ex.Message}");
        }
    }

    public List<string> GetExpandedFolders()
    {
        List<string> expandedFolders = new();

        void VisitFolder(string path, FolderResource folder)
        {
            bool isRoot = folder == _rootFolder;

            if (!isRoot && !folder.Expanded)
            {
                // Don't descend into unexpanded folders
                return;
            }

            // Build the path to the folder (leaving out the root part).
            string newPath = string.Empty;
            if (!isRoot)
            {
                if (string.IsNullOrEmpty(path))
                {
                    newPath = folder.Name;
                }
                else
                {
                    newPath = path + "/" + folder.Name;
                }
            }

            if (!string.IsNullOrEmpty(newPath))
            {
                expandedFolders.Add(newPath);
            }
            foreach (var resource in folder.Children)
            {
                if (resource is FolderResource childFolder)
                {
                    VisitFolder(newPath, childFolder);
                }
            }
        }

        VisitFolder(string.Empty, _rootFolder);

        return expandedFolders;
    }

    public void SetExpandedFolders(List<string> expandedFolders)
    {
        // Convert the folder list to a hash set for faster lookup
        var folderSet = new HashSet<string>(expandedFolders);

        void VisitFolder(string path, FolderResource folder)
        {
            bool isRoot = folder == _rootFolder;

            // Build the path to the folder (leaving out the root part).
            string newPath = string.Empty;
            if (!isRoot)
            {
                if (string.IsNullOrEmpty(path))
                {
                    newPath = folder.Name;
                }
                else
                {
                    newPath = path + "/" + folder.Name;
                }
            }

            // Expand this folder if it's in the expanded folder set
            if (!string.IsNullOrEmpty(newPath) &&
                folderSet.Contains(newPath))
            {
                folder.Expanded = true;
            }

            // Recursively visit every child folder resource
            foreach (var resource in folder.Children)
            {
                if (resource is FolderResource childFolder)
                {
                    VisitFolder(newPath, childFolder);
                }
            }
        }

        VisitFolder(string.Empty, _rootFolder);
    }
}