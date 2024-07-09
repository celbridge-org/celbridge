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
        void AddResourcePathSegment(Resource resource)
        {
            if (resource.ParentFolder is null)
            {
                return;
            }

            // Build path by recursively visiting each parent folders
            AddResourcePathSegment(resource.ParentFolder);

            // The trick is to append the path segment after we've visited the parent.
            // This ensures the path segments are appended in the right order.
            if (sb.Length > 0)
            {
                sb.Append("/");
            }
            sb.Append(resource.Name);
        }
        AddResourcePathSegment(pathResource);

        var path = sb.ToString();

        return path;
    }

    public string GetPath(IResource resource)
    {
        var resourcePath = GetResourcePath(resource);

        var path = Path.Combine(_projectFolder, resourcePath);

        // Unify directory separators
        return Path.GetFullPath(path);
    }

    public Result<IResource> GetResource(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return Result<IResource>.Fail("Failed to get resource. Resource path is empty.");
        }

        var segments = resourcePath.Split('/');
        var rootFolderResource = _rootFolder;

        // Attempt to match each path segment with the corresponding resource in the tree
        var segmentIndex = 0;
        while (segmentIndex < segments.Length)
        {
            FolderResource? matchingFolder = null;
            string segment = segments[segmentIndex];
            foreach (var childResource in rootFolderResource.Children)
            {
                if (childResource is FolderResource childFolder &&
                    childFolder.Name == segment)
                {
                    if (segmentIndex == segments.Length - 1)
                    {
                        // The folder name matches the last segment in the path, so this is the
                        // folder resource we're looking for.
                        return Result<IResource>.Ok(childFolder);
                    }

                    // This folder resource matches a subfolder in the path, so we can move onto
                    // searching for the next segment.
                    matchingFolder = childFolder;
                    break;
                }
                else if (childResource is FileResource childFile &&
                         childFile.Name == segment &&
                         segmentIndex == segments.Length - 1)
                {
                    // The file name matches the last segment in the path, so this is the
                    // file resource we're looking for.
                    return Result<IResource>.Ok(childFile);
                }
            }

            if (matchingFolder is null)
            {
                break;
            }

            rootFolderResource = matchingFolder;
            segmentIndex++;
        }

        return Result<IResource>.Fail($"Failed to find a resource matching the path '{resourcePath}'.");
    }

    public Result UpdateRegistry()
    {
        var createResult = CreateFolderResource(_projectFolder);
        if (createResult.IsFailure)
        {
            return Result.Fail(createResult.Error);
        }

        var newRootFolder = createResult.Value;

        UpdateFolderResource(_rootFolder, newRootFolder);

        return Result.Ok();
    }

    /// <summary>
    // Updates the changed parts of the resource tree, leaving unmodified parts of the tree untouched.
    /// </summary>
    private Result UpdateFolderResource(FolderResource oldFolderResource, FolderResource newFolderResource)
    {
        try
        {
            // Update files
            var currentFiles = oldFolderResource.Children.OfType<FileResource>().ToList();
            var newFiles = newFolderResource.Children.OfType<FileResource>().ToList();

            // Remove deleted files
            foreach (var file in currentFiles)
            {
                if (!newFiles.Any(f => f.Name == file.Name))
                {
                    oldFolderResource.Children.Remove(file);
                }
            }

            // Add new files
            foreach (var file in newFiles)
            {
                if (!currentFiles.Any(f => f.Name == file.Name))
                {
                    // Insert file resource in sorted order
                    InsertResource(oldFolderResource.Children, file);
                }
            }

            // Update folders
            var currentFolders = oldFolderResource.Children.OfType<FolderResource>().ToList();
            var newFolders = newFolderResource.Children.OfType<FolderResource>().ToList();

            foreach (var folder in currentFolders)
            {
                var correspondingNewFolder = newFolders.FirstOrDefault(f => f.Name == folder.Name);
                if (correspondingNewFolder == null)
                {
                    // Folder deleted
                    oldFolderResource.Children.Remove(folder);
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
                    InsertResource(oldFolderResource.Children, folder);
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to update folder resource. {ex.Message}");
        }

        return Result.Ok();
    }

    private void InsertResource(ObservableCollection<IResource> collection, IResource resource)
    {
        int index = 0;
        while (index < collection.Count)
        {
            IResource item = collection[index];

            if (resource is FolderResource && 
                item is not FolderResource)
            {
                // Folders appear before files
                break;
            }

            if (resource.GetType() == item.GetType() && 
                string.Compare(resource.Name, item.Name, StringComparison.InvariantCulture) < 0)
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
                return Result<FolderResource>.Fail($"Failed to create folder resource. Path not found '{path}'");
            }

            //
            // Create a new folder resource to represent the folder at this path
            //
            var newFolderResource = new FolderResource(Path.GetFileName(path), parentFolder);

            //
            // Create a new folder resource for each descendant folder and add it as a child of the new folder resource.
            //
            var sortedFolders = Directory.GetDirectories(path).OrderBy(d => d).ToList();
            foreach (var folder in sortedFolders)
            {
                var scanResult = CreateFolderResource(folder, newFolderResource);
                if (scanResult.IsFailure)
                {
                    return scanResult;
                }
                var resource = scanResult.Value;

                newFolderResource.AddChild(resource);
            }

            //
            // Create a new file resource for each descendent file and add it as a child of the folder resource.
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
            return Result<FolderResource>.Fail($"Failed to create folder resource '{path}'. {ex.Message}");
        }
    }

    public List<string> GetExpandedFolders()
    {
        List<string> expandedFolders = new();

        void VisitFolder(string resourcePath, FolderResource folder)
        {
            bool isRoot = folder == _rootFolder;

            if (!isRoot && !folder.Expanded)
            {
                // Don't descend into unexpanded folders
                return;
            }

            // Build the path to the folder (leaving out the root part).
            string newResourcePath = string.Empty;
            if (!isRoot)
            {
                if (string.IsNullOrEmpty(resourcePath))
                {
                    newResourcePath = folder.Name;
                }
                else
                {
                    newResourcePath = resourcePath + "/" + folder.Name;
                }
            }

            if (!string.IsNullOrEmpty(newResourcePath))
            {
                expandedFolders.Add(newResourcePath);
            }
            foreach (var resource in folder.Children)
            {
                if (resource is FolderResource childFolder)
                {
                    VisitFolder(newResourcePath, childFolder);
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

        void VisitFolder(string resourcePath, FolderResource folder)
        {
            bool isRoot = folder == _rootFolder;

            // Build the path to the folder (leaving out the root part).
            string newResourcePath = string.Empty;
            if (!isRoot)
            {
                if (string.IsNullOrEmpty(resourcePath))
                {
                    newResourcePath = folder.Name;
                }
                else
                {
                    newResourcePath = resourcePath + "/" + folder.Name;
                }
            }

            // Expand this folder if it's in the expanded folder set
            if (!string.IsNullOrEmpty(newResourcePath) &&
                folderSet.Contains(newResourcePath))
            {
                folder.Expanded = true;
            }

            // Recursively visit every child folder resource
            foreach (var resource in folder.Children)
            {
                if (resource is FolderResource childFolder)
                {
                    VisitFolder(newResourcePath, childFolder);
                }
            }
        }

        VisitFolder(string.Empty, _rootFolder);
    }

    public bool IsFolderExpanded(string resourcePath)
    {
        var getResult = GetResource(resourcePath);
        if (getResult.IsFailure)
        {
            return false;
        }

        var folderResource = getResult.Value as FolderResource;
        if (folderResource is null)
        {
            return false;
        }

        return folderResource.Expanded;
    }
}