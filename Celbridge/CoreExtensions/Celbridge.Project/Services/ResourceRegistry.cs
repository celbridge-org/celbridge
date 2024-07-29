using Celbridge.Resources;
using Celbridge.Projects.Models;
using System.Text;

namespace Celbridge.Projects.Services
{
    public class ResourceRegistry : IResourceRegistry
    {
        private readonly IMessengerService _messengerService;

        public string ProjectFolderPath { get; set; } = string.Empty;

        private FolderResource _rootFolder = new FolderResource(string.Empty, null);

        public IFolderResource RootFolder => _rootFolder;

        public ResourceRegistry(IMessengerService messengerService)
        {
            _messengerService = messengerService;
        }

        public ResourceKey GetResourceKey(IResource resource)
        {
            try
            {
                var sb = new StringBuilder();
                void AddResourceKeySegment(IResource resource)
                {
                    if (resource.ParentFolder is null ||
                        resource.ParentFolder.ParentFolder is null)
                    {
                        return;
                    }

                    // Build path by recursively visiting each parent folders
                    AddResourceKeySegment(resource.ParentFolder);

                    // The trick is to append the path segment after we've visited the parent.
                    // This ensures the path segments are appended in the right order.
                    if (sb.Length > 0)
                    {
                        sb.Append("/");
                    }
                    sb.Append(resource.Name);
                }
                AddResourceKeySegment(resource);

                var resourceKey = new ResourceKey(sb.ToString());

                return resourceKey;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to get resource key for '{resource}'", ex);
            }
        }

        public Result<ResourceKey> GetResourceKey(string resourcePath)
        {
            try
            {
                var normalizedPath = Path.GetFullPath(resourcePath);
                var normalizedProjectPath = Path.GetFullPath(ProjectFolderPath);

                if (!normalizedPath.StartsWith(normalizedProjectPath))
                {
                    return Result<ResourceKey>.Fail($"The path '{resourcePath}' is not in the project folder '{ProjectFolderPath}'.");
                }

                var resourceKey = normalizedPath.Substring(ProjectFolderPath.Length)
                    .Replace('\\', '/')
                    .Trim('/');

                return Result<ResourceKey>.Ok(resourceKey);
            }
            catch (Exception ex)
            {
                return Result<ResourceKey>.Fail($"Failed to get resource key for '{resourcePath}'. {ex.Message}");
            }
        }

        public string GetResourcePath(IResource resource)
        {
            try
            {
                var resourceKey = GetResourceKey(resource);
                var path = GetResourcePath(resourceKey);
                return path;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to get path for resource '{resource}'", ex);
            }
        }

        public string GetResourcePath(ResourceKey resource)
        {
            try
            {
                var resourcePath = Path.Combine(ProjectFolderPath, resource);
                var normalized = Path.GetFullPath(resourcePath);

                return normalized;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to get path for resource '{resource}'.", ex);
            }
        }

        public Result<IResource> GetResource(ResourceKey resource)
        {
            if (resource.IsEmpty)
            {
                // An empty resource key refers to the root folder
                return Result<IResource>.Ok(_rootFolder);
            }

            var segments = resource.ToString().Split('/');
            var searchFolder = _rootFolder;

            // Attempt to match each segment with the corresponding resource in the tree
            var segmentIndex = 0;
            while (segmentIndex < segments.Length)
            {
                FolderResource? matchingFolder = null;
                string segment = segments[segmentIndex];
                foreach (var childResource in searchFolder.Children)
                {
                    if (childResource is FolderResource childFolder &&
                        childFolder.Name == segment)
                    {
                        if (segmentIndex == segments.Length - 1)
                        {
                            // The folder name matches the last segment in the key, so this is the
                            // folder resource we're looking for.
                            return Result<IResource>.Ok(childFolder);
                        }

                        // This folder resource matches this segment in the key, so we can move onto
                        // searching for the next segment.
                        matchingFolder = childFolder;
                        break;
                    }
                    else if (childResource is FileResource childFile &&
                             childFile.Name == segment &&
                             segmentIndex == segments.Length - 1)
                    {
                        // The file name matches the last segment in the key, so this is the
                        // file resource we're looking for.
                        return Result<IResource>.Ok(childFile);
                    }
                }

                if (matchingFolder is null)
                {
                    break;
                }

                searchFolder = matchingFolder;
                segmentIndex++;
            }

            return Result<IResource>.Fail($"Failed to find a resource matching the resource key '{resource}'.");
        }

        public ResourceKey GetCopyDestinationResource(ResourceKey sourceResource, ResourceKey destResource)
        {
            string output = destResource;

            var getResult = GetResource(destResource);
            if (getResult.IsSuccess)
            {
                var resource = getResult.Value;
                if (resource is IFolderResource)
                {
                    if (destResource.IsEmpty)
                    {
                        // Destination is the root folder
                        output = sourceResource.ResourceName;
                    }
                    else
                    {
                        if (sourceResource == destResource)
                        {
                            // Source and destination are the same folder. This case is allowed because
                            // the user may duplicate a folder by copying and pasting it to the same destination.
                            output = destResource;
                        }
                        else
                        {
                            // Destination is a folder, so append the source resource name to this folder.
                            output = destResource.Combine(sourceResource.ResourceName);
                        }
                    }
                }
            }

            return output;
        }

        public ResourceKey GetContextMenuItemFolder(IResource? resource)
        {
            IFolderResource? destFolder = null;
            switch (resource)
            {
                case IFolderResource folder:
                    destFolder = folder;
                    break;
                case IFileResource file:
                    destFolder = file.ParentFolder;
                    break;
            }
            if (destFolder is null)
            {
                destFolder = _rootFolder;
            }

            return GetResourceKey(destFolder);
        }

        public Result UpdateResourceRegistry()
        {
            var createResult = CreateFolderResource(ProjectFolderPath, _rootFolder);
            if (createResult.IsFailure)
            {
                return Result.Fail(createResult.Error);
            }

            var newRootFolder = createResult.Value;
            newRootFolder.Name = string.Empty;

            MergeChildren(_rootFolder, newRootFolder);

            // Remove any expanded folders that no longer exist
            ExpandedFolders.Remove(string.Empty);
            ExpandedFolders.Remove((expandedFolder) =>
            {
                return GetResource(expandedFolder).IsFailure;
            });

            var message = new ResourceRegistryUpdatedMessage();
            _messengerService.Send(message);

            return Result.Ok();
        }

        private Result<FolderResource> CreateFolderResource(string newFolderPath, FolderResource? parentFolder = null)
        {
            try
            {
                if (!Directory.Exists(newFolderPath))
                {
                    return Result<FolderResource>.Fail($"Failed to create folder resource. Path not found '{newFolderPath}'");
                }

                //
                // Create a new resource key to represent the folder at this path
                //
                var newFolderResource = new FolderResource(Path.GetFileName(newFolderPath), parentFolder);
                var newFolderResourceKey = GetResourceKey(newFolderResource);

                // Set the expanded state of the folder resource
                newFolderResource.IsExpanded = ExpandedFolders.Contains(newFolderResourceKey);

                //
                // Create a new folder resource for each descendant folder and add it as a child of the new folder resource.
                //
                var subFolderPaths = Directory.GetDirectories(newFolderPath).OrderBy(d => d).ToList();
                foreach (var subFolderPath in subFolderPaths)
                {
                    var scanResult = CreateFolderResource(subFolderPath, newFolderResource);
                    if (scanResult.IsFailure)
                    {
                        return scanResult;
                    }
                    var subFolderResource = scanResult.Value;

                    newFolderResource.AddChild(subFolderResource);
                }

                //
                // Create a new file resource for each descendent file and add it as a child of the folder resource.
                //
                var filePaths = Directory.GetFiles(newFolderPath).OrderBy(f => f).ToList();
                foreach (var filePath in filePaths)
                {
                    var newFileResource = new FileResource(Path.GetFileName(filePath), newFolderResource);
                    newFolderResource.AddChild(newFileResource);
                }

                return Result<FolderResource>.Ok(newFolderResource);
            }
            catch (Exception ex)
            {
                return Result<FolderResource>.Fail($"Failed to create folder resource '{newFolderPath}'. {ex.Message}");
            }
        }

        private void MergeChildren(FolderResource existingFolder, FolderResource newFolder)
        {
            var existingChildren = existingFolder.Children.ToDictionary(child => child.Name);
            existingFolder.Children.Clear();

            // Todo: Check if this handles case where a folder is deleted and a
            // file with the same name is added at the same time.

            foreach (var newChild in newFolder.Children)
            {
                if (existingChildren.TryGetValue(newChild.Name, out var existingChild))
                {
                    if (existingChild is FolderResource existingChildFolder && newChild is FolderResource newChildFolder)
                    {
                        MergeChildren(existingChildFolder, newChildFolder);
                        existingFolder.AddChild(existingChildFolder);
                    }
                    else
                    {
                        existingFolder.AddChild(newChild);
                    }
                }
                else
                {
                    existingFolder.AddChild(newChild);
                }
            }

            existingFolder.Children = existingFolder.Children
                .OrderBy(child => child is IFolderResource ? 0 : 1)
                .ThenBy(child => child.Name)
                .ToList();
        }

        public List<string> ExpandedFolders { get; } = new();

        public void SetFolderIsExpanded(ResourceKey folderResource, bool isExpanded)
        {
            if (isExpanded)
            {
                if (!folderResource.IsEmpty &&
                    !ExpandedFolders.Contains(folderResource))
                {
                    ExpandedFolders.Add(folderResource);
                    ExpandedFolders.Sort();
                }
            }
            else
            {
                ExpandedFolders.Remove(folderResource);
            }
        }

        public bool IsFolderExpanded(ResourceKey folderResource)
        {
            return ExpandedFolders.Contains(folderResource);
        }
    }
}
