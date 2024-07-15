using Celbridge.BaseLibrary.Resources;
using Celbridge.Project.Models;
using CommunityToolkit.Diagnostics;
using System.Text;

namespace Celbridge.Project.Services;

public class ResourceRegistry : IResourceRegistry
{
    private readonly string _projectFolderPath;

    public IFolderResource RootFolder { get; } = new FolderResource(string.Empty, null);

    public ResourceRegistry(string projectFolderPath)
    {
        _projectFolderPath = projectFolderPath;
    }

    public string GetResourcePath(IResource resource)
    {
        var pathResource = resource as Resource;
        Guard.IsNotNull(pathResource);

        var sb = new StringBuilder();
        void AddResourcePathSegment(IResource resource)
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

        var path = Path.Combine(_projectFolderPath, resourcePath);

        // Unify directory separators
        return Path.GetFullPath(path);
    }

    public Result<IResource> GetResource(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            // An empty resource path refers to the root folder
            return Result<IResource>.Ok(RootFolder);
        }

        var segments = resourcePath.Split('/');
        var rootFolderResource = RootFolder;

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

    public Result UpdateResourceTree()
    {
        var createResult = CreateFolderResource(_projectFolderPath);
        if (createResult.IsFailure)
        {
            return Result.Fail(createResult.Error);
        }
        var newRootFolder = createResult.Value;

        RootFolder.Children.ReplaceWith(newRootFolder.Children);

        // Remove any expanded folders that no longer exist
        ExpandedFolders.Remove((expandedFolder) => 
        {
            return GetResource(expandedFolder).IsFailure;
        });

        return Result.Ok();
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
            var newFolderResourcePath = GetResourcePath(newFolderResource);

            // Set the expanded state of the folder resource
            newFolderResource.IsExpanded = ExpandedFolders.Contains(newFolderResourcePath);

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

    public List<string> ExpandedFolders { get; } = new();

    public void SetFolderIsExpanded(string resourcePath, bool isExpanded)
    {
        if (isExpanded)
        {
            if (!ExpandedFolders.Contains(resourcePath))
            {
                ExpandedFolders.Add(resourcePath);
                ExpandedFolders.Sort();
            }
        }
        else
        {
            ExpandedFolders.Remove(resourcePath);
        }
    }

    public bool IsFolderExpanded(string resourcePath)
    {
        return ExpandedFolders.Contains(resourcePath);
    }
}